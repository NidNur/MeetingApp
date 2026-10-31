using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MeetingApp.Api.Services;

public class ProfileImageStorage
{
    private const int MaxFileSize = 2 * 1024 * 1024;

    private readonly string _directory;
    private readonly ILogger<ProfileImageStorage> _logger;

    public ProfileImageStorage(
        IWebHostEnvironment environment,
        ILogger<ProfileImageStorage> logger)
    {
        _directory = Path.Combine(
            environment.ContentRootPath,
            "Storage",
            "ProfileImages");

        _logger = logger;
    }

    public async Task<string> SaveAsync(
        IFormFile image,
        CancellationToken cancellationToken)
    {
        if (image.Length == 0)
        {
            throw new ValidationException("Profil resmi boş olamaz.");
        }

        if (image.Length > MaxFileSize)
        {
            throw new ValidationException("Profil resmi en fazla 2 MB olabilir.");
        }

        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();

        if (extension is not (".jpg" or ".jpeg" or ".png"))
        {
            throw new ValidationException("Profil resmi JPG veya PNG olmalıdır.");
        }

        await using var input = image.OpenReadStream();
        var buffer = new byte[MaxFileSize + 1];
        var length = await input.ReadAtLeastAsync(
            buffer,
            buffer.Length,
            throwOnEndOfStream: false,
            cancellationToken: cancellationToken);

        if (length == 0)
        {
            throw new ValidationException("Profil resmi boş olamaz.");
        }

        if (length > MaxFileSize)
        {
            throw new ValidationException("Profil resmi en fazla 2 MB olabilir.");
        }

        var isPng = length >= 8
            && buffer.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var isJpeg = length >= 3
            && buffer[0] == 255
            && buffer[1] == 216
            && buffer[2] == 255;

        if ((extension == ".png" && !isPng)
            || (extension is ".jpg" or ".jpeg" && !isJpeg))
        {
            throw new ValidationException(
                "Dosyanın içeriği JPG veya PNG biçimiyle uyuşmuyor.");
        }

        var fileName = $"{Guid.NewGuid():N}{(isPng ? ".png" : ".jpg")}";
        var path = Path.Combine(_directory, fileName);

        Directory.CreateDirectory(_directory);

        var fileCreated = false;

        try
        {
            await using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.Asynchronous))
            {
                fileCreated = true;
                await output.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
            }

            return fileName;
        }
        catch
        {
            if (fileCreated)
            {
                TryDelete(fileName);
            }

            throw;
        }
    }

    public void TryDelete(string? fileName)
    {
        if (fileName is null)
        {
            return;
        }

        var extension = Path.GetExtension(fileName);

        if (Path.GetFileName(fileName) != fileName
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _)
            || extension is not (".jpg" or ".png"))
        {
            throw new ArgumentException("Geçersiz profil resmi adı.", nameof(fileName));
        }

        try
        {
            File.Delete(Path.Combine(_directory, fileName));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Profil resmi silinemedi: {FileName}", fileName);
        }
    }
}
