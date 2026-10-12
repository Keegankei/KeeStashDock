using System.Buffers.Binary;
using System.IO.Compression;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ImageSnippetTests
{
    private static readonly string TempParent = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "Images");
    private string _root = null!;
    private FileLogger _log = null!;
    private Db _db = null!;
    private SnippetRepository _repo = null!;
    private SnippetService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(TempParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Logs"));
        _log = new FileLogger(Path.Combine(_root, "Logs"));
        _db = new Db(Path.Combine(_root, "images.db"));
        new MigrationRunner(_db, _log).Run();
        _repo = new SnippetRepository(_db);
        _service = new SnippetService(_repo, _log);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _log.Dispose();
        SqliteConnection.ClearAllPools();
        string resolved = Path.GetFullPath(_root);
        Assert.AreEqual(Path.GetFullPath(TempParent), Path.GetDirectoryName(resolved));
        Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(resolved), "N", out _));
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }

    [TestMethod]
    public void ImageWithoutOcr_RoundTripsOriginalAndThumbnailWhileListsContainOnlyMetadata()
    {
        byte[] png = Png(2, 3, 41), thumbnail = Png(1, 1, 41);
        long id = _service.AddImage(png, thumbnail, title: "Screenshot");
        Snippet item = _service.AllLive().Single();
        Assert.IsTrue(item.IsImage);
        Assert.AreEqual(SnippetSource.Screenshot, item.Source);
        Assert.AreEqual(string.Empty, item.Content);
        Assert.AreEqual(PngImageValidator.ImageHash(png), item.ContentHash);
        CollectionAssert.AreEqual(png, _service.GetImage(id));
        CollectionAssert.AreEqual(thumbnail, _service.GetImageThumbnail(id));
        Assert.AreEqual(4, new DatabaseSchemaValidator().Validate(_db.DatabaseFile, true).SchemaVersion);
    }

    [TestMethod]
    public void ImageIdentitySurvivesOcrEditsAndIsIndependentOfTextEntries()
    {
        byte[] png = Png(2, 2, 1);
        long first = _service.AddImage(png, png);
        Snippet item = _service.GetById(first)!;
        string identity = item.ContentHash;
        item.Content = "recognized unique words";
        _service.Update(item, ["ocr"]);
        Assert.AreEqual(identity, item.ContentHash);
        Assert.AreEqual(first, _service.AddImage(png, png));
        Assert.AreEqual("recognized unique words", _service.GetById(first)!.Content);
        long second = _service.AddImage(Png(2, 2, 2), png, ocrText: item.Content);
        long text = _service.Add(item.Content, SnippetSource.Manual);
        Assert.AreNotEqual(first, second);
        Assert.AreNotEqual(first, text);
        Assert.HasCount(3, _service.AllLive());
        Assert.AreEqual(first, _repo.SearchFts("recognized", 10).Single(s => s.Id == first).Id);
        item.Content = "";
        _service.Update(item);
        Assert.AreEqual("", _service.GetById(first)!.Content);
        Assert.AreEqual(identity, _service.GetById(first)!.ContentHash);
        Assert.HasCount(2, _repo.SearchFts("recognized", 10));
    }

    [TestMethod]
    public void RepositoryImageEditIgnoresForgedHashAndType()
    {
        byte[] png = Png();
        long id = _service.AddImage(png, png);
        var draft = new Snippet { Id = id, Content = "", ContentHash = "changed", Type = SnippetType.PlainText };
        _repo.UpdateContent(draft, DateTime.UtcNow);
        Assert.AreEqual(PngImageValidator.ImageHash(png), _service.GetById(id)!.ContentHash);
        Assert.IsTrue(_service.GetById(id)!.IsImage);
        Assert.ThrowsExactly<ArgumentException>(() => _service.Add("image", SnippetSource.Manual, explicitType: SnippetType.Image));
    }

    [TestMethod]
    public void TrashRestoreAndPermanentDeletionKeepThenRemoveBothAssets()
    {
        byte[] png = Png();
        long id = _service.AddImage(png, png);
        _service.MoveToTrash(id);
        Assert.HasCount(0, _service.AllLive());
        CollectionAssert.AreEqual(png, _service.GetImage(id));
        _service.RestoreFromTrash(id);
        Assert.AreEqual(id, _service.AllLive().Single().Id);
        _service.DeleteForever(id);
        Assert.IsNull(_service.GetImage(id));
        Assert.IsNull(_service.GetImageThumbnail(id));
        Assert.AreEqual(0L, Scalar("SELECT COUNT(*) FROM snippet_images"));
    }

    [TestMethod]
    public void HousekeepingExpiryPurgeAndEmptyTrashCascadeAssets()
    {
        byte[] png = Png();
        long id = _service.AddImage(png, png, expire: ExpirePolicy.OneDay);
        Execute("UPDATE snippets SET expire_at = '2000-01-01T00:00:00.0000000Z'");
        Assert.AreEqual(1, _repo.MoveExpiredToTrash());
        Assert.IsNotNull(_service.GetImage(id));
        Execute("UPDATE snippets SET deleted_at = '2000-01-01T00:00:00.0000000Z'");
        Assert.AreEqual(1, _repo.PurgeTrash(30));
        Assert.IsNull(_service.GetImage(id));
        id = _service.AddImage(png, png);
        _service.MoveToTrash(id);
        _service.EmptyTrash();
        Assert.IsNull(_service.GetImage(id));
    }

    [TestMethod]
    public void BurnAfterReadImageRemainsRecoverable()
    {
        byte[] png = Png();
        long id = _service.AddImage(png, png, expire: ExpirePolicy.BurnAfterRead);
        Assert.IsTrue(_service.RegisterUse(_service.GetById(id)!));
        Assert.AreEqual(id, _service.Trash().Single().Id);
        CollectionAssert.AreEqual(png, _service.GetImage(id));
    }

    [TestMethod]
    public void MigrationFromV3PreservesTextAndMakesImagesAvailable()
    {
        long textId = _service.Add("legacy searchable text", SnippetSource.Manual);
        Execute("DROP TABLE snippet_images; UPDATE schema_version SET version=3; PRAGMA user_version=3;");
        new MigrationRunner(_db, _log).Run();
        Assert.AreEqual(textId, _service.AllLive().Single().Id);
        Assert.AreEqual("legacy searchable text", _service.GetById(textId)!.Content);
        byte[] png = Png();
        _service.AddImage(png, png);
        Assert.AreEqual(4, new DatabaseSchemaValidator().Validate(_db.DatabaseFile, true).SchemaVersion);
    }

    [TestMethod]
    public void FullPackageImportRetainsImageAndTrashAndCreatesCompleteSafetyBackup()
    {
        byte[] png = Png();
        long sourceId = _service.AddImage(png, png, ocrText: "backup OCR");
        _service.MoveToTrash(sourceId);
        string package = Path.Combine(_root, "images.ksdpkg");
        new BackupService(_db, _log).ExportPackage(package);
        var maintenance = new DatabaseMaintenanceCoordinator();
        var targetDb = new Db(Path.Combine(_root, "target.db"), maintenance);
        new MigrationRunner(targetDb, _log).Run();
        var targetService = new SnippetService(new SnippetRepository(targetDb), _log);
        byte[] original = Png(1, 1, 90);
        targetService.AddImage(original, original);
        var importer = new DatabaseImportService(targetDb, maintenance, new BackupService(targetDb, _log),
            new DatabaseSchemaValidator(), new PhysicalDatabaseFileOperations(), new SuccessfulRestarter(), _log);
        DatabaseImportResult result = importer.Import(package);
        var imported = new SnippetService(new SnippetRepository(new Db(targetDb.DatabaseFile)), _log);
        Assert.AreEqual("backup OCR", imported.Trash().Single().Content);
        CollectionAssert.AreEqual(png, imported.GetImage(sourceId));
        var safety = new SnippetRepository(new Db(result.SafetyBackupFile));
        CollectionAssert.AreEqual(original, safety.GetImage(safety.GetAllLive().Single().Id));
    }

    [TestMethod]
    public void GroupV2RoundTripRetainsImagesEmptyOcrAndTypeWhileV1StillImports()
    {
        var categories = new CategoryRepository(_db);
        long category = categories.Insert(new Category { Name = "Images" });
        byte[] png = Png();
        long id = _service.AddImage(png, png, category);
        _service.Add("code example", SnippetSource.Manual, categoryId: category, explicitType: SnippetType.Code);
        var groups = new GroupIoService(_service, categories);
        string file = Path.Combine(_root, "images.ksdgrp");
        groups.ExportGroup(category, "Images", file);
        var package = groups.ReadPackage(file);
        Assert.AreEqual(2, package.Version);
        CollectionAssert.AreEqual(png, package.Items.Single(i => i.Type == SnippetType.Image).ImagePng);
        _service.DeleteForever(id);
        Assert.AreEqual(2, groups.ImportGroup(package, "Imported", null));
        Snippet imported = _service.ByCategory(categories.FindByName("Imported")!.Id).Single(s => s.IsImage);
        CollectionAssert.AreEqual(png, _service.GetImage(imported.Id));
        Assert.AreEqual("", imported.Content);
        Assert.IsTrue(_service.ByCategory(categories.FindByName("Imported")!.Id).Any(s => s.Type == SnippetType.Code));
        File.WriteAllText(file, """{"version":1,"name":"Legacy","items":[{"title":null,"content":"legacy package"}]}""");
        Assert.AreEqual(1, groups.ImportGroup(groups.ReadPackage(file), "Legacy", null));
    }

    [TestMethod]
    public void MalformedImagePackageRejectsBeforeOverwriteOrNewCategoryMutation()
    {
        var categories = new CategoryRepository(_db);
        long category = categories.Insert(new Category { Name = "Keep" });
        long id = _service.Add("unchanged", SnippetSource.Manual, categoryId: category);
        var groups = new GroupIoService(_service, categories);
        var package = new GroupIoService.GroupPackage(2, "Broken",
            [new(null, "valid earlier item"), new(null, "", SnippetType.Image, [1, 2, 3], Png())]);
        Assert.ThrowsExactly<InvalidDataException>(() => groups.ImportGroup(package, "Keep", category));
        Assert.ThrowsExactly<InvalidDataException>(() => groups.ImportGroup(package, "New", null));
        Assert.HasCount(1, categories.GetAll());
        Assert.AreEqual(id, _service.AllLive().Single().Id);
        Assert.HasCount(0, _service.Trash());
    }

    [TestMethod]
    [DataRow("hash")]
    [DataRow("png")]
    [DataRow("missing")]
    [DataRow("dimensions")]
    public void DatabaseValidationRejectsDamagedImageBeforeImport(string damage)
    {
        byte[] png = Png();
        _service.AddImage(png, png);
        Execute(damage switch
        {
            "hash" => "UPDATE snippets SET content_hash='bad'",
            "png" => "UPDATE snippet_images SET png=x'010203'",
            "missing" => "DELETE FROM snippet_images",
            _ => "UPDATE snippet_images SET width=2",
        });
        Assert.ThrowsExactly<DatabaseValidationException>(() => new DatabaseSchemaValidator().Validate(_db.DatabaseFile, true));
    }

    [TestMethod]
    public void InvalidPngDimensionsChecksumCompressionAndThumbnailRejectBeforeAnyInsert()
    {
        byte[] png = Png();
        byte[] badCrc = (byte[])png.Clone(); badCrc[^1] ^= 1;
        Assert.ThrowsExactly<InvalidDataException>(() => _service.AddImage(badCrc, png));
        Assert.ThrowsExactly<InvalidDataException>(() => _service.AddImage(Png(17000, 1), png));
        Assert.ThrowsExactly<InvalidDataException>(() => _service.AddImage(png, Png(513, 1)));
        Assert.ThrowsExactly<InvalidDataException>(() => _service.AddImage(Png(1, 2, truncatePixels: true), png));
        Assert.ThrowsExactly<InvalidDataException>(() => _service.AddImage([137, 80, 78], png));
        Assert.HasCount(0, _service.AllLive());
        Assert.AreEqual(0L, Scalar("SELECT COUNT(*) FROM snippet_images"));
    }

    private void Execute(string sql)
    {
        using var connection = _db.Open(); using var command = connection.CreateCommand();
        command.CommandText = sql; command.ExecuteNonQuery();
    }
    private long Scalar(string sql)
    {
        using var connection = _db.Open(); using var command = connection.CreateCommand();
        command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar());
    }
    private sealed class SuccessfulRestarter : IApplicationRestarter { public bool TryRestart() => true; }

    private static byte[] Png(int width = 1, int height = 1, byte color = 17, bool truncatePixels = false)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR"u8, header);
        using var encoded = new MemoryStream();
        using (var zlib = new ZLibStream(encoded, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] row = Enumerable.Repeat(color, width * 4 + 1).ToArray(); row[0] = 0;
            for (int y = 0; y < (truncatePixels ? height - 1 : height); y++) zlib.Write(row);
        }
        Chunk(output, "IDAT"u8, encoded.ToArray()); Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }
    private static void Chunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> size = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, data.Length);
        stream.Write(size); stream.Write(type); stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in type.ToArray().Concat(data.ToArray()))
        {
            crc ^= value;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(size, ~crc); stream.Write(size);
    }
}
