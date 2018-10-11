﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Prototype
{
    // utilities for parsing gRPC messages from request/response streams
    // NOTE: implementations are not efficient and are very GC heavy
    public class StreamUtils
    {
        const int MessageDelimiterSize = 4;  // how many bytes it takes to encode "Message-Length"

        // reads a grpc message
        // returns null if there are no more messages.
        public static async Task<byte[]> ReadMessageAsync(Stream stream)
        {
            // read Compressed-Flag and Message-Length
            // as described in https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md
            var delimiterBuffer = new byte[1 + MessageDelimiterSize];
            if (!await ReadExactlyBytesOrNothing(stream, delimiterBuffer, 0, delimiterBuffer.Length))
            {
                return null;
            }

            var compressionFlag = delimiterBuffer[0];
            var messageLength = DecodeMessageLength(delimiterBuffer, 1);

            if (compressionFlag != 0)
            {
                // TODO(jtattermusch): support compressed messages
                throw new IOException("Compressed messages are not yet supported.");
            }

            var msgBuffer = new byte[messageLength];
            if (!await ReadExactlyBytesOrNothing(stream, msgBuffer, 0, msgBuffer.Length))
            {
                throw new IOException("Unexpected end of stream.");
            }
            return msgBuffer;
        }

        // reads exactly the number of requested bytes (returns true if successfully read).
        // returns false if we reach end of stream before successfully reading anything.
        // throws if stream ends in the middle of reading.
        private static async Task<bool> ReadExactlyBytesOrNothing(Stream stream, byte[] buffer, int offset, int count)
        {
            bool noBytesRead = true;
            while (count > 0)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset, count);
                if (bytesRead == 0)
                {
                    if (noBytesRead)
                    {
                        return false;
                    }
                    throw new IOException("Unexpected end of stream.");
                }
                noBytesRead = false;
                offset += bytesRead;
                count -= bytesRead;
            }
            return true;
        }

        private static int DecodeMessageLength(byte[] buffer, int offset)
        {
            if (buffer.Length < offset + MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to decode message length.");
            }

            ulong result = 0;
            for (int i = offset; i < offset + MessageDelimiterSize; i++)
            {
                // msg length stored in big endian
                result = result * 0x100 + buffer[i];
            }

            if (result > int.MaxValue)
            {
                throw new IOException("Message too large: " + result);
            }
            return (int) result;
        }
    }
}