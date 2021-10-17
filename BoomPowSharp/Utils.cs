using Blake2Fast;
using System;
using System.Collections.Generic;
using System.Text;

namespace BoomPowSharp
{
    class Utils
    {
        public static bool NanoValidateWork(ReadOnlySpan<byte> BlockHash, ulong Difficulty, ReadOnlySpan<byte> Work)
        {
            Span<byte> Digest = stackalloc byte[8];
            Span<byte> Input = stackalloc byte[40];

            Work.CopyTo(Input);
            BlockHash.CopyTo(Input[8..]);

            Blake2b.ComputeAndWriteHash(8, Input, Digest);

            return BitConverter.ToUInt64(Digest) >= Difficulty;
        }

        public static byte[] FromHex(string Hex)
        {
            byte[] Bytes = new byte[Hex.Length / 2];
            
            for (int i = 0; i < Hex.Length / 2; i++)
            {
                Bytes[i] = Convert.ToByte(Hex.Substring(i * 2, 2), 16);
            }

            return Bytes;
        }
    }
}
