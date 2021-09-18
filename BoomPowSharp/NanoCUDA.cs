using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using ManagedCuda;
using Xoshiro.PRNG64;

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
            _NanoWorkerKernel.GridDimensions = 1048576 / 512;
        }

        public ulong WorkGenerate(byte[] BlockHash, ulong Difficulty, CancellationToken Cancellation = default(CancellationToken))
        {
            _Context.SetCurrent();

            XoRoShiRo128plus Random = new XoRoShiRo128plus();

            CudaStream Stream = new CudaStream();

            CudaDeviceVariable<byte>   BlockHashBuffer = new CudaDeviceVariable<byte>(32, Stream);
            CudaDeviceVariable<ulong>  ResultBuffer    = new CudaDeviceVariable<ulong>(1, Stream);

            BlockHashBuffer.AsyncCopyToDevice(BlockHash, Stream);
            ResultBuffer.MemsetAsync(0, Stream.Stream);

            ulong RandomNonce = 0;
            ulong Result      = 0;

            var Rand = new Random();

            while (Result == 0 && !Cancellation.IsCancellationRequested)
            {
                RandomNonce = Random.Next64U();
                _NanoWorkerKernel.RunAsync(Stream.Stream, RandomNonce, ResultBuffer.DevicePointer, BlockHashBuffer.DevicePointer, Difficulty);
                Stream.Synchronize();
                ResultBuffer.CopyToHost(ref Result);
            }

            return Result;
        }
    }
}
