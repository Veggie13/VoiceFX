using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;

namespace MultithreadedStream
{
    /// <summary>
    /// Multithreaded buffer where one thread can write and many threads can read simultaneously. 
    /// </summary>
    public class MemoryStreamMultiplexer : IDisposable
    {
        private ManualResetEvent[] _dataReadyEvents = new ManualResetEvent[255];
        private ManualResetEvent[] _finishedEvents = new ManualResetEvent[255];
        private int _readerCount = 0;

        private bool _finished;
        private int _Length;
        private List<byte[]> _Buffer = new List<byte[]>();

        public int Length { get { return _Length; } }

        public MemoryStreamMultiplexer()
        {

        }

        public void Write(byte[] data, int pos, int length)
        {
            byte[] newBuf = new byte[length];
            Buffer.BlockCopy(data, pos, newBuf, 0, length);
            lock (_Buffer)
            {
                _Buffer.Add(newBuf);
                _Length += length;
            }
            Set();
        }

        private void Set()
        {
            for (int i = 0; i < _readerCount; i++)
                _dataReadyEvents[i].Set();
        }

        public void Finish()
        {
            for (int i = 0; i < _readerCount; i++)
                _finishedEvents[i].Set();
            _finished = true;
        }

        public MemoryStreamReader GetReader()
        {
            ManualResetEvent dataReady = new ManualResetEvent(_finished);
            ManualResetEvent finished = new ManualResetEvent(_finished);
            lock (_dataReadyEvents)
            {
                _dataReadyEvents[_readerCount] = dataReady;
                _finishedEvents[_readerCount] = finished;
                _readerCount++;
            }
            return new MemoryStreamReader(_Buffer, dataReady, finished);
        }

        private bool disposed = false;
        public void Dispose()
        {
            if (!disposed)
            {
                Finish();

                for (int i = 0; i < _readerCount; i++)
                {
                    _dataReadyEvents[i].Dispose();
                    _finishedEvents[i].Dispose();
                }
                _readerCount = 0;

                disposed = true;
            }
        }
    }

    public class MemoryStreamReader : Stream, IDisposable
    {
        private int _position;
        private int _bufferIndex;
        private int _bufferPos;
        private List<byte[]> _bufferList;

        private ManualResetEvent[] _waitHandles;
        private ManualResetEvent _dataReady;
        private ManualResetEvent _finished;

        public MemoryStreamReader(List<byte[]> bufferList, ManualResetEvent dataReady, ManualResetEvent finished)
        {
            _waitHandles = new ManualResetEvent[] { dataReady, finished };
            _bufferList = bufferList;
            _dataReady = dataReady;
            _finished = finished;
            _bufferPos = 0;
            _bufferIndex = 0;
            _position = 0;
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        public override long Position
        {
            get
            {
                return _position;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_bufferIndex < _bufferList.Count)
            {
                return ReadInternal(buffer, offset, count);
            }
            else
            {
                _dataReady.Reset();
                // Wait for either data ready event of the finished event.
                int index = WaitHandle.WaitAny(_waitHandles, TimeSpan.FromSeconds(30), false);
                // either of the event fired. see if there's more data to read.
                if (_bufferIndex < _bufferList.Count)
                    return ReadInternal(buffer, offset, count);
                else
                    return 0;   // No more bytes will be available. Finished.
            }
        }

        private int ReadInternal(byte[] buffer, int offset, int count)
        {
            byte[] currentBuffer = _bufferList[_bufferIndex];

            if (_bufferPos + count <= currentBuffer.Length)
            {
                // the current buffer holds the same or more bytes than what is asked for
                // So, give what was asked.
                Buffer.BlockCopy(currentBuffer, _bufferPos, buffer, offset, count);

                _bufferPos += count;
                _position += count;
                return count;
            }
            else
            {
                // current buffer does not have the necessary bytes. deliver whatever is available.
                if (_bufferPos < currentBuffer.Length)
                {
                    int remainingBytes = currentBuffer.Length - _bufferPos;
                    Buffer.BlockCopy(currentBuffer, _bufferPos, buffer, offset, remainingBytes);

                    _position += remainingBytes;
                    _bufferIndex++;
                    _bufferPos = 0;

                    // Try to read from the next buffer in the list and deliver
                    // the undelivered bytes. The Read call might block and wait for 
                    // remaining bytes to appear. 
                    return remainingBytes +
                        this.Read(buffer, offset + remainingBytes, count - remainingBytes);
                }
                else
                {
                    // Already all bytes from currnet buffer has been delivered. Try next buffer.
                    _bufferIndex++;
                    _bufferPos = 0;

                    // There may not be next buffer and thus we will have to wait.
                    return this.Read(buffer, offset, count);
                }
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public new void Dispose()
        {
            _dataReady = null;
            _finished = null;
            _bufferList = null;
            _waitHandles = null;
        }
    }
}
