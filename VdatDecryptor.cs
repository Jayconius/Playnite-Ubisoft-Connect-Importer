using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UbisoftConnectImporter
{
    /// <summary>Decrypts Ubisoft's protobuf envelope using the user's local DPAPI key.</summary>
    internal static class VdatDecryptor
    {
        private const string StoreDirectory = "Ubisoft Game Launcher\\store";

        public static byte[] Decrypt(byte[] file)
        {
            byte[] envelope = null;
            foreach (var field in ReadFields(file, 0, file.Length))
                if (field.Number == 1 && field.Wire == 2) { envelope = Slice(file, field.Offset, field.Length); break; }
            if (envelope == null) throw new InvalidDataException("The Ubisoft VDAT envelope is missing.");

            byte[] header = null, ciphertext = null;
            foreach (var field in ReadFields(envelope, 0, envelope.Length))
            {
                if (field.Number == 1 && field.Wire == 2) header = Slice(envelope, field.Offset, field.Length);
                else if (field.Number == 2 && field.Wire == 2) ciphertext = Slice(envelope, field.Offset, field.Length);
            }
            if (header == null || ciphertext == null) throw new InvalidDataException("The Ubisoft VDAT header or ciphertext is missing.");

            byte[] cryptoInfo = null;
            int version = 0;
            foreach (var field in ReadFields(header, 0, header.Length))
            {
                if (field.Number == 1 && field.Wire == 0) version = checked((int)field.Varint);
                else if (field.Number == 2 && field.Wire == 2) cryptoInfo = Slice(header, field.Offset, field.Length);
            }
            if (version != 1 || cryptoInfo == null) throw new InvalidDataException("Unsupported Ubisoft VDAT header version.");

            int algorithm = 0;
            string keyId = null;
            byte[] nonce = null, tag = null;
            foreach (var field in ReadFields(cryptoInfo, 0, cryptoInfo.Length))
            {
                if (field.Number == 1 && field.Wire == 0) algorithm = checked((int)field.Varint);
                else if (field.Number == 2 && field.Wire == 2) keyId = Encoding.ASCII.GetString(cryptoInfo, field.Offset, field.Length);
                else if (field.Number == 3 && field.Wire == 2) nonce = Slice(cryptoInfo, field.Offset, field.Length);
                else if (field.Number == 4 && field.Wire == 2) tag = Slice(cryptoInfo, field.Offset, field.Length);
            }
            if (algorithm != 1 || string.IsNullOrWhiteSpace(keyId) || nonce == null || tag == null)
                throw new InvalidDataException("Unsupported Ubisoft VDAT cryptographic parameters.");
            if (keyId.Length != 32 || nonce.Length != 12 || tag.Length != 16)
                throw new InvalidDataException("Invalid Ubisoft VDAT key identifier, nonce, or tag length.");

            var keyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StoreDirectory, keyId + ".bin");
            if (!File.Exists(keyPath)) throw new FileNotFoundException("The DPAPI key for this Ubisoft VDAT cache is missing.", keyPath);
            var key = ProtectedData.Unprotect(File.ReadAllBytes(keyPath), null, DataProtectionScope.CurrentUser);
            if (key.Length != 32) throw new CryptographicException("Ubisoft VDAT did not yield an AES-256 key.");
            return DecryptAesGcm(key, nonce, tag, ciphertext);
        }

        private static byte[] DecryptAesGcm(byte[] key, byte[] nonce, byte[] tag, byte[] ciphertext)
        {
            IntPtr algorithm = IntPtr.Zero, keyHandle = IntPtr.Zero, keyObject = IntPtr.Zero;
            var pins = new List<GCHandle>();
            try
            {
                var keyPin = Pin(key, pins);
                var noncePin = Pin(nonce, pins);
                var tagPin = Pin(tag, pins);
                var inputPin = Pin(ciphertext, pins);
                var output = new byte[ciphertext.Length];
                var outputPin = Pin(output, pins);

                Check(BCryptOpenAlgorithmProvider(out algorithm, "AES", null, 0));
                var mode = Encoding.Unicode.GetBytes("ChainingModeGCM\0");
                Check(BCryptSetProperty(algorithm, "ChainingMode", mode, mode.Length, 0));
                var lengthBuffer = new byte[4];
                var lengthPin = Pin(lengthBuffer, pins);
                int returned;
                Check(BCryptGetProperty(algorithm, "ObjectLength", lengthPin.AddrOfPinnedObject(), lengthBuffer.Length, out returned, 0));
                var objectLength = BitConverter.ToInt32(lengthBuffer, 0);
                keyObject = Marshal.AllocHGlobal(objectLength);
                Check(BCryptGenerateSymmetricKey(algorithm, out keyHandle, keyObject, objectLength, keyPin.AddrOfPinnedObject(), key.Length, 0));

                var authInfo = new AuthenticatedCipherModeInfo
                {
                    Size = Marshal.SizeOf(typeof(AuthenticatedCipherModeInfo)),
                    Version = 1,
                    Nonce = noncePin.AddrOfPinnedObject(),
                    NonceLength = nonce.Length,
                    Tag = tagPin.AddrOfPinnedObject(),
                    TagLength = tag.Length
                };
                int written;
                Check(BCryptDecrypt(keyHandle, inputPin.AddrOfPinnedObject(), ciphertext.Length, ref authInfo,
                    IntPtr.Zero, 0, outputPin.AddrOfPinnedObject(), output.Length, out written, 0));
                if (written != output.Length) Array.Resize(ref output, written);
                return output;
            }
            finally
            {
                if (keyHandle != IntPtr.Zero) BCryptDestroyKey(keyHandle);
                if (algorithm != IntPtr.Zero) BCryptCloseAlgorithmProvider(algorithm, 0);
                if (keyObject != IntPtr.Zero) Marshal.FreeHGlobal(keyObject);
                foreach (var pin in pins) if (pin.IsAllocated) pin.Free();
            }
        }

        private static GCHandle Pin(byte[] value, List<GCHandle> pins)
        {
            var pin = GCHandle.Alloc(value, GCHandleType.Pinned);
            pins.Add(pin);
            return pin;
        }

        private static void Check(int status)
        {
            if (status < 0) throw new CryptographicException("Windows CNG AES-GCM operation failed (0x" + status.ToString("X8") + ").");
        }

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            var value = new byte[length];
            Buffer.BlockCopy(data, offset, value, 0, length);
            return value;
        }

        private sealed class WireField
        {
            public int Number, Wire, Offset, Length;
            public ulong Varint;
        }

        private static IEnumerable<WireField> ReadFields(byte[] data, int start, int length)
        {
            var end = checked(start + length);
            var pos = start;
            while (pos < end)
            {
                var tag = ReadVarint(data, ref pos, end);
                var field = new WireField { Number = (int)(tag >> 3), Wire = (int)(tag & 7) };
                if (field.Number == 0) throw new InvalidDataException("Invalid protobuf field number in Ubisoft VDAT file.");
                switch (field.Wire)
                {
                    case 0: field.Varint = ReadVarint(data, ref pos, end); break;
                    case 1: field.Offset = pos; field.Length = 8; pos += 8; break;
                    case 2:
                        var size = ReadVarint(data, ref pos, end);
                        if (size > int.MaxValue || pos + (long)size > end) throw new InvalidDataException("Invalid protobuf field length in Ubisoft VDAT file.");
                        field.Offset = pos; field.Length = (int)size; pos += (int)size; break;
                    case 5: field.Offset = pos; field.Length = 4; pos += 4; break;
                    default: throw new InvalidDataException("Unsupported protobuf wire type in Ubisoft VDAT file.");
                }
                if (pos > end) throw new InvalidDataException("Truncated protobuf field in Ubisoft VDAT file.");
                yield return field;
            }
        }

        private static ulong ReadVarint(byte[] data, ref int pos, int end)
        {
            ulong value = 0;
            var shift = 0;
            while (pos < end && shift < 64)
            {
                var item = data[pos++];
                value |= (ulong)(item & 0x7f) << shift;
                if ((item & 0x80) == 0) return value;
                shift += 7;
            }
            throw new InvalidDataException("Invalid protobuf varint in Ubisoft VDAT file.");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AuthenticatedCipherModeInfo
        {
            public int Size, Version;
            public IntPtr Nonce;
            public int NonceLength;
            public IntPtr AuthData;
            public int AuthDataLength;
            public IntPtr Tag;
            public int TagLength;
            public IntPtr MacContext;
            public int MacContextLength, AadLength;
            public long DataLength;
            public int Flags;
        }

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm, string algorithmId, string implementation, int flags);
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptSetProperty(IntPtr handle, string property, byte[] input, int inputLength, int flags);
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptGetProperty(IntPtr handle, string property, IntPtr output, int outputLength, out int resultLength, int flags);
        [DllImport("bcrypt.dll")]
        private static extern int BCryptGenerateSymmetricKey(IntPtr algorithm, out IntPtr key, IntPtr keyObject, int keyObjectLength, IntPtr secret, int secretLength, int flags);
        [DllImport("bcrypt.dll")]
        private static extern int BCryptDecrypt(IntPtr key, IntPtr input, int inputLength, ref AuthenticatedCipherModeInfo paddingInfo, IntPtr iv, int ivLength, IntPtr output, int outputLength, out int resultLength, int flags);
        [DllImport("bcrypt.dll")]
        private static extern int BCryptDestroyKey(IntPtr key);
        [DllImport("bcrypt.dll")]
        private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, int flags);
    }
}
