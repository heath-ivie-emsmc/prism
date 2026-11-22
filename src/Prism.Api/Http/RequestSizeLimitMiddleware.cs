using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;

namespace Prism.Api.Http;

/// <summary>
/// Enforces the body size an endpoint declares with <see cref="IRequestSizeLimitMetadata"/>, which minimal APIs do not enforce on their own.
/// A declared length over the cap is refused without reading anything. Otherwise the real number of bytes read is bounded too, because
/// a client can declare a small length and send more, or send chunked with no length at all. Runs after routing so the endpoint is known.
/// </summary>
public sealed class RequestSizeLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>() is { MaxRequestBodySize: { } max })
        {
            if (context.Request.ContentLength > max)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            IHttpMaxRequestBodySizeFeature? feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = max;
            }
            else
            {
                context.Request.Body = new LimitedReadStream(context.Request.Body, max);
            }
        }

        await next(context);
    }

    /// <summary>Passes reads through and throws a 413 once more than the limit has been read.</summary>
    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long total;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            total += read;
            return total > limit
                ? throw new BadHttpRequestException("The request body is too large.", StatusCodes.Status413PayloadTooLarge)
                : read;
        }
    }
}
