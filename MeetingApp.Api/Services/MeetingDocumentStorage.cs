using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MeetingApp.Api.Services;

public record StoredMeetingDocument(string FileName, string OriginalName);

public class MeetingDocumentStorage
{
    private const int MaxFileSize = 10 * 1024 * 1024;
    private readonly string _directory;
    private readonly ILogger<MeetingDocumentStorage> _logger;

    public MeetingDocumentStorage(
        IWebHostEnvironment environment,
        ILogger<MeetingDocumentStorage> logger)
    {
        _directory = Path.Combine(environment.ContentRootPath, "Storage", "MeetingDocuments");
        _logger = logger;
    }

    public async Task<StoredMeetingDocument> SaveAsync(IFormFile document, CancellationToken cancellationToken)
    {
        if (document.Length <= 0)
            throw new ValidationException("Doküman boş olamaz.");
        if (document.Length > MaxFileSize)
            throw new ValidationException("Doküman en fazla 10 MB olabilir.");

        var originalName = Path.GetFileName(document.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 255
            || originalName.Any(char.IsControl))
            throw new ValidationException("Dokümanın dosya adı geçersiz veya çok uzun.");

        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx"))
            throw new ValidationException("Doküman PDF veya DOCX olmalıdır.");

        await using var input = document.OpenReadStream();
        var buffer = new byte[MaxFileSize + 1];
        var length = await input.ReadAtLeastAsync(buffer, buffer.Length,
            throwOnEndOfStream: false, cancellationToken: cancellationToken);
        if (length == 0)
            throw new ValidationException("Doküman boş olamaz.");
        if (length > MaxFileSize)
            throw new ValidationException("Doküman en fazla 10 MB olabilir.");

        if (extension == ".pdf")
        {
            if (length < 5 || !buffer.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                throw new ValidationException("Dosyanın içeriği PDF biçimiyle uyuşmuyor.");
        }
        else
        {
            ValidateDocx(buffer, length);
        }

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var path = GetPath(fileName);
        Directory.CreateDirectory(_directory);
        var fileCreated = false;
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                fileCreated = true;
                await output.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
            }
            return new StoredMeetingDocument(fileName, originalName);
        }
        catch
        {
            if (fileCreated)
                TryDelete(fileName);
            throw;
        }
    }

    private static void ValidateDocx(byte[] buffer, int length)
    {
        const long maxExpandedSize = 50L * 1024 * 1024;
        try
        {
            using var stream = new MemoryStream(buffer, 0, length, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count is 0 or > 1000)
                throw new ValidationException("DOCX dokümanı çok fazla bölüm içeriyor.");

            long expandedSize = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length > maxExpandedSize - expandedSize)
                    throw new ValidationException("DOCX dokümanının açılmış boyutu çok büyük.");
                expandedSize += entry.Length;
                if (!names.Add(entry.FullName) || entry.FullName.Contains('\\')
                    || entry.FullName.StartsWith('/') || entry.FullName.Split('/').Contains("..")
                    || entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("DOCX dokümanının bölümleri geçersiz.");
            }

            var contentTypes = archive.GetEntry("[Content_Types].xml");
            var mainDocument = archive.GetEntry("word/document.xml");
            if (contentTypes is null || contentTypes.Length is 0 or > 65536
                || mainDocument is null || mainDocument.Length == 0)
                throw new ValidationException("Dosyanın içeriği DOCX biçimiyle uyuşmuyor.");

            using var xmlStream = contentTypes.Open();
            using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 65536
            });
            var types = XDocument.Load(reader);
            XNamespace typesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
            var validMainPart = types.Root?.Name == typesNamespace + "Types"
                && types.Root.Elements(typesNamespace + "Override").Any(part =>
                    (string?)part.Attribute("PartName") == "/word/document.xml"
                    && (string?)part.Attribute("ContentType")
                        == "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
            if (!validMainPart)
                throw new ValidationException("Dosyanın içeriği DOCX biçimiyle uyuşmuyor.");
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            throw new ValidationException("DOCX dokümanının içeriği okunamıyor.", exception);
        }
    }

    public FileStream? OpenRead(string fileName)
    {
        var path = GetPath(fileName);
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public string GetContentType(string fileName)
    {
        GetPath(fileName);
        return Path.GetExtension(fileName) == ".pdf" ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }

    public bool TryDelete(string? fileName)
    {
        if (fileName is null)
            return true;
        var path = GetPath(fileName);
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Toplantı dokümanı silinemedi: {FileName}", fileName);
            return false;
        }
    }

    private string GetPath(string fileName)
    {
        if (Path.GetFileName(fileName) != fileName
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _)
            || Path.GetExtension(fileName) is not (".pdf" or ".docx"))
            throw new ArgumentException("Geçersiz toplantı dokümanı adı.", nameof(fileName));
        return Path.Combine(_directory, fileName);
    }
}
