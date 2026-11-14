namespace Ct007.Tests.Fixtures;

/// <summary>A fresh folder under %TEMP%\ct007-tests for one test; deleted (with SQLite -wal/-shm files) on dispose.</summary>
public sealed class TestFolder : IDisposable
{
    // Rechecked after the run: background work can recreate a deleted folder, and leaked connections keep files locked.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Created = new();

    public TestFolder()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ct007-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Created.Enqueue(Root);
    }

    public string Root { get; }

    public string Path(string name) => System.IO.Path.Combine(Root, name);

    /// <summary>Hash of a file that another connection may still hold open (SQLite shares read and write access).</summary>
    public static string Sha256(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    public void Dispose()
    {
        // Pooled connections keep the files open; release them before deleting. If SQLite itself could not load
        // (TypeInitializationException), there are no connections to release: still delete the folder.
        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch (TypeInitializationException)
        {
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Deletes every test folder that still exists once all tests have run; returns how many remain.</summary>
    public static int DeleteLeftovers()
    {
        // Finalizes connections the app under test leaked so their files can be deleted.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch (TypeInitializationException)
        {
        }

        var remaining = 0;

        while (Created.TryDequeue(out var root))
        {
            try
            {
                if (Directory.Exists(root))
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Temp folder not deleted: {root} ({error.Message})");
                remaining++;
            }
        }

        return remaining;
    }
}
