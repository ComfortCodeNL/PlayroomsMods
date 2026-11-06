using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PlayroomsCoop
{
    internal static class SaveGuard
    {
        private const int MaxText = 128 * 1024 * 1024;

        private static readonly Regex Dangerous = new Regex(
            @"(^|[\[\],\s""':;{(])(System\.(IO|Diagnostics|Reflection|Runtime|Net|Security|Threading|Web|Xml|Data|Configuration|CodeDom|Management|Windows|Resources|Linq\.Expressions|Delegate|MulticastDelegate|Action|Func|Type|Activator|AppDomain|Environment|Console)\b|Microsoft\.|Mono\.|UnityEngine\.(Application|Networking|Windows|Scripting)\b|BepInEx|HarmonyLib|Il2Cpp)",
            RegexOptions.CultureInvariant);

        public static string Check(byte[] data, object settings)
        {
            string text;
            try { text = Decode(data, settings); }
            catch (Exception e) { return "couldn't read it as a Playrooms save (" + e.Message + ")"; }

            int i = 0;
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == '﻿')) i++;
            if (i >= text.Length || text[i] != '{') return "it isn't a Playrooms save file";

            string plain = Unescape(text);
            var hit = Dangerous.Match(plain);
            if (hit.Success)
            {
                int from = Math.Max(0, hit.Index - 20), len = Math.Min(plain.Length - from, 100);
                return "it contains a type a save never needs: " + SteamNet.Plain(plain.Substring(from, len));
            }
            return null;
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf("\\u", StringComparison.Ordinal) < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 5 < s.Length && s[i + 1] == 'u' &&
                    int.TryParse(s.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
                { sb.Append((char)code); i += 5; continue; }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static string Decode(byte[] data, object settings)
        {
            byte[] b = data;
            int enc = 0; string password = null;
            if (settings != null)
            {
                try { enc = Convert.ToInt32(R.Get(settings, "encryptionType")); } catch { }
                try { password = R.Get<string>(settings, "encryptionPassword"); } catch { }
            }
            if (enc != 0)
            {
                byte[] d = null;
                try { d = Decrypt(b, password ?? ""); } catch { }
                if (d == null || !Plausible(d)) d = GameDecrypt(b, password ?? "");
                b = d;
            }
            if (b.Length > 2 && b[0] == 0x1F && b[1] == 0x8B) b = Gunzip(b);
            return Encoding.UTF8.GetString(b);
        }

        private static bool Plausible(byte[] d)
        {
            if (d.Length > 2 && d[0] == 0x1F && d[1] == 0x8B) return true;
            foreach (byte x in d) { if (x == (byte)'{') return true; if (x != ' ' && x != '\n' && x != '\r' && x != '\t' && x != 0xEF && x != 0xBB && x != 0xBF) return false; }
            return false;
        }

        private static byte[] Decrypt(byte[] data, string password)
        {
            if (data.Length < 32) throw new InvalidDataException("too short");
            var iv = new byte[16];
            Buffer.BlockCopy(data, 0, iv, 0, 16);
            using (var aes = Aes.Create())
            using (var kdf = new Rfc2898DeriveBytes(password, iv, 100))
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                aes.IV = iv; aes.Key = kdf.GetBytes(16);
                using (var dec = aes.CreateDecryptor())
                    return dec.TransformFinalBlock(data, 16, data.Length - 16);
            }
        }

        private static byte[] GameDecrypt(byte[] data, string password)
        {
            var es3 = R.T("ES3") ?? throw new InvalidDataException("no Easy Save");
            var mi = es3.GetMethod("DecryptBytes") ?? throw new InvalidDataException("no DecryptBytes");
            var arrType = mi.GetParameters()[0].ParameterType; // Il2CppStructArray<byte>
            var arr = Activator.CreateInstance(arrType, new object[] { data });
            var outArr = mi.Invoke(null, new[] { arr, (object)password }) as System.Collections.IEnumerable;
            if (outArr == null) throw new InvalidDataException("couldn't decrypt");
            using (var ms = new MemoryStream())
            {
                foreach (var x in outArr) ms.WriteByte(Convert.ToByte(x));
                return ms.ToArray();
            }
        }

        private static byte[] Gunzip(byte[] data)
        {
            using (var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    output.Write(buf, 0, n);
                    if (output.Length > MaxText) throw new InvalidDataException("too large");
                }
                return output.ToArray();
            }
        }
    }
}
