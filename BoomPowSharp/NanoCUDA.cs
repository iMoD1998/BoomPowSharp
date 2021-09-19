using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using ManagedCuda;
using Xoshiro.PRNG64;
using Blake2Fast;

namespace BoomPowSharp
{
    class NanoCUDA
    {
        CudaContext                _Context;
        CudaKernel                 _NanoWorkerKernel;

        public NanoCUDA(int DeviceID)
        {
            _Context = new CudaContext(DeviceID, false);

            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BoomPowSharp.work.ptx");

            _NanoWorkerKernel = _Context.LoadKernelPTX(stream, "nano_work");

            _NanoWorkerKernel.BlockDimensions = 512;
            _NanoWorkerKernel.GridDimensions  = 1048576 / 512;
        }

        public bool WorkValidate(ReadOnlySpan<byte> BlockHash, ulong Difficulty, ReadOnlySpan<byte> Work)
        {
            Span<byte> Digest = stackalloc byte[8];
            Span<byte> Input  = stackalloc byte[40];

            Work.CopyTo(Input);
            BlockHash.CopyTo(Input[8..]);

            Blake2b.ComputeAndWriteHash(8, Input, Digest);

            return BitConverter.ToUInt64(Digest) >= Difficulty;
        }

        static ulong NumberOfSetBits(ulong i)
        {
            i = i - ((i >> 1) & 0x5555555555555555);
            i = (i & 0x3333333333333333) + ((i >> 2) & 0x3333333333333333);
            return (((i + (i >> 4)) & 0xF0F0F0F0F0F0F0F) * 0x101010101010101) >> 56;
        }

        public ulong WorkGenerate(byte[] BlockHash, ulong Difficulty, CancellationToken Cancellation = default(CancellationToken))
        {
            _Context.SetCurrent();

            //
            // Lower number = better priority
            //
            CudaStream Stream = new CudaStream(64 - ((int)NumberOfSetBits(Difficulty)));

            CudaDeviceVariable<byte>   BlockHashBuffer = new CudaDeviceVariable<byte>(32, Stream);
            CudaDeviceVariable<ulong>  ResultBuffer    = new CudaDeviceVariable<ulong>(1, Stream);

            BlockHashBuffer.AsyncCopyToDevice(BlockHash, Stream);

            XoRoShiRo128starstar Random = new XoRoShiRo128starstar();

            ulong Result = 0;
        
            while (Result == 0 && !Cancellation.IsCancellationRequested)
            {
                ulong RandomNonce = Random.Next64U();
                
                _NanoWorkerKernel.RunAsync(Stream.Stream, RandomNonce, ResultBuffer.DevicePointer, BlockHashBuffer.DevicePointer, Difficulty);

                ResultBuffer.CopyToHost(ref Result);
            }

            if (Result == 0)
                return 0;

            if (!WorkValidate(BlockHash, Difficulty, BitConverter.GetBytes(Result)))
                return 0;

            return Result;
        }
    }
}
