using System.IO.Compression;
using Microsoft.AspNetCore.Http.Features;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for container environment
builder.WebHost.ConfigureKestrel(options =>
{
    // Disable request size limit
    options.Limits.MaxRequestBodySize = null;
    
    // Disable minimum response rate requirements - prevents automatic client disconnection on slow networks
    options.Limits.MinResponseDataRate = null;
    
    // Limit the number of concurrent connections to prevent resource exhaustion in K8s pod
    options.Limits.MaxConcurrentConnections = 100;
});

var app = builder.Build();

// Configure minimal pipeline for performance
app.UseRouting();

// Endpoint to serve folder as zip download
app.MapGet("/download/{folderName}", async (string folderName, HttpContext context, bool calculateLength = true) =>
{
    try
    {
        // Base directory - customize this path as needed
        string baseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Folders");
        string folderPath = Path.Combine(baseDirectory, folderName);

        // Validate path and check if directory exists
        if (!Directory.Exists(folderPath) || 
            !Path.GetFullPath(folderPath).StartsWith(Path.GetFullPath(baseDirectory)))
        {
            return Results.NotFound($"Folder '{folderName}' not found or access denied.");
        }

        // Set response headers for optimal download handling
        context.Response.ContentType = "application/zip";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{folderName}.zip\"";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
        
        // Explicitly disable response buffering for Kestrel
        var responseBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
        if (responseBodyFeature != null)
        {
            responseBodyFeature.DisableBuffering();
        }

        // Set a cancellation token that combines client disconnects with a reasonable timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromHours(1));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, timeoutCts.Token);
        var cancellationToken = linkedCts.Token;

        // Calculate content length only if requested and feasible
        if (calculateLength)
        {
            try
            {
                // Use timeout for content length calculation to prevent hanging (20 seconds)
                using var contentLengthCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var contentLengthLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, contentLengthCts.Token);
                
                long contentLength = await CalculateZipContentLengthAsync(folderPath, contentLengthLinkedCts.Token);
                context.Response.Headers.ContentLength = contentLength;
            }
            catch (OperationCanceledException)
            {
                // Return specific error for timeout
                Console.Error.WriteLine($"Content length calculation timed out for folder: {folderName}");
                return Results.Problem(
                    title: "Processing Error",
                    detail: "The folder is too large to process in a reasonable time",
                    statusCode: 413); // 413 = Payload Too Large
            }
            catch (Exception ex)
            {
                // Log general errors but don't expose details to client
                Console.Error.WriteLine($"Error calculating content length: {ex.Message}");
                return Results.Problem(
                    title: "Processing Error", 
                    statusCode: 500);
            }
        }

        // Stream the zip archive directly to the client
        await StreamZipArchiveAsync(folderPath, context.Response.Body, cancellationToken);
        
        // Ensure final flush
        await context.Response.Body.FlushAsync(cancellationToken);
        
        return Results.Empty;
    }
    catch (OperationCanceledException)
    {
        // Client disconnected - we'll return right away
        context.Abort();
        return Results.Empty;
    }
    catch (Exception ex)
    {
        // Log the error, but don't expose details to client
        Console.Error.WriteLine($"Error serving zip: {ex.Message}");
        return Results.Problem("An error occurred while generating the download", statusCode: 500);
    }
});

app.Run();

// Content length calculation with timeout
static async Task<long> CalculateZipContentLengthAsync(string folderPath, CancellationToken cancellationToken)
{
    var countingStream = new CountingStream();
    await StreamZipArchiveAsync(folderPath, countingStream, cancellationToken);
    return countingStream.Length;
}

// Optimized zip streaming for Kubernetes container environment
static async Task StreamZipArchiveAsync(string folderPath, Stream outputStream, CancellationToken cancellationToken)
{
    using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);
    
    var folder = new DirectoryInfo(folderPath);
    var baseOffset = folder.FullName.Length + 1;

    // Get directory info for file enumeration
    // Using EnumerateFiles to avoid loading all paths into memory at once
    var allFiles = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);
    
    // Optimization: Don't create more buffers than necessary
    const int bufferSize = 65536; // 64KB seems to be a sweet spot for most environments
    byte[] buffer = new byte[bufferSize];
    
    int filesProcessed = 0;
    long totalBytesWritten = 0;
    DateTime lastResourceCheck = DateTime.UtcNow;
    
    foreach (var file in allFiles)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var fileInfo = new FileInfo(file);
        var entryName = file.Substring(baseOffset);
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        
        using var entryStream = entry.Open();
        using var fileStream = new FileStream(
            file, 
            FileMode.Open, 
            FileAccess.Read, 
            FileShare.Read, 
            bufferSize, 
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        int bytesRead;
        int unflushedBytes = 0;
        
        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await entryStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            
            unflushedBytes += bytesRead;
            totalBytesWritten += bytesRead;
            
            // Flush more frequently with smaller files, less frequently with large files
            // Small files: flush after about 512KB
            // Large files (>500MB): flush after about 4MB
            int flushThreshold = fileInfo.Length > 500_000_000 ? 4_194_304 : 524_288;
            
            if (unflushedBytes >= flushThreshold)
            {
                await entryStream.FlushAsync(cancellationToken);
                unflushedBytes = 0;
                
                // Also monitor total bytes between output stream flushes
                if (totalBytesWritten >= 20_971_520) // 20MB
                {
                    await outputStream.FlushAsync(cancellationToken);
                    totalBytesWritten = 0;
                    
                    // After significant output is flushed, give a small yield for system to process
                    await Task.Delay(1, cancellationToken);
                }
            }
            
            // Periodically check system resources (roughly every 100MB processed)
            if (totalBytesWritten > 104_857_600 && (DateTime.UtcNow - lastResourceCheck).TotalSeconds > 5)
            {
                // If available memory is getting low, force GC
                if (Environment.WorkingSet > 200_000_000) // 200MB working set as a threshold
                {
                    GC.Collect(0, GCCollectionMode.Optimized, false);
                }
                lastResourceCheck = DateTime.UtcNow;
            }
        }
        
        // Always flush after each file
        await entryStream.FlushAsync(cancellationToken);
        
        filesProcessed++;
        if (filesProcessed % 20 == 0) // Every 20 files
        {
            await outputStream.FlushAsync(cancellationToken);
        }
    }
}

// CountingStream to measure size without storing data
public class CountingStream : Stream
{
    private long _length;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;
    public override long Position { get => _length; set => throw new NotSupportedException(); }

    public override void Flush() { }
    
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    
    public override void SetLength(long value) => throw new NotSupportedException();
    
    public override void Write(byte[] buffer, int offset, int count) => _length += count;

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _length += count;
        return Task.CompletedTask;
    }
}
