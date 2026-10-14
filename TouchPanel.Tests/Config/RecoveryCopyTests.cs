using System.IO;
using System.Text.Json;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Emergency recovery copy on a shutdown save failure (F-08/RF-077): the same JSON a save would
/// write lands in <c>panel.recovery.json</c> atomically, without touching <c>panel.json</c> or the
/// backup; a successful save never writes one; a canceled or locked attempt fails bounded and
/// silent.
/// </summary>
public class RecoveryCopyTests
{
    [Fact]
    public async Task TryWriteRecoveryCopyAsync_WritesTheExpectedContentAtomically()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        var config = DefaultConfigGenerator.Create();
        config.Window.AlwaysOnTop = true;
        config.Pages[0].Widgets[0].X = 321;

        var written = await store.TryWriteRecoveryCopyAsync(config);

        Assert.True(written);
        Assert.True(File.Exists(temp.Config.RecoveryFile));
        Assert.False(File.Exists(temp.Config.RecoveryFile + ".tmp"));
        var recovery = Deserialize(File.ReadAllText(temp.Config.RecoveryFile));
        Assert.True(recovery.Window.AlwaysOnTop);
        Assert.Equal(321, recovery.Pages[0].Widgets[0].X);

        // The copy never touches the user-editable file or the last-known-valid backup.
        Assert.False(File.Exists(temp.Config.PanelFile));
        Assert.False(File.Exists(temp.Config.BackupFile));
    }

    [Fact]
    public async Task SaveAsync_Success_DoesNotWriteARecoveryCopy()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        Assert.True((await store.SaveAsync(DefaultConfigGenerator.Create())).Succeeded);

        Assert.True(File.Exists(temp.Config.PanelFile));
        Assert.False(File.Exists(temp.Config.RecoveryFile));
    }

    [Fact]
    public async Task ShutdownAfterAFailedLayoutSave_WritesTheRecoveryCopyWithTheEditedGeometry()
    {
        using var temp = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
        var store = new JsonConfigStore(temp.Config, migrations: null, timeProvider: null, logger: CreateLogger());
        var service = new EditModeService(config, page, store);

        // Seed panel.json, then lock it so the edit-mode save fails (locked file / disk-full shape).
        Assert.True((await store.SaveAsync(config)).Succeeded);
        service.Enter(1024, 600);

        // The 236 DIP action button has room to move; the full-width title would clamp to x=32.
        var button = page.Widgets[1];
        Assert.True(service.MoveWidget(button, 300, 200));

        EditModeSaveResult exit;
        using (new FileStream(temp.Config.PanelFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            exit = await service.ExitAsync();
        }

        Assert.True(exit.Attempted);
        Assert.False(exit.Succeeded);

        // F-08: the shutdown path attempts the recovery copy when the dirty-layout save failed.
        Assert.True(await store.TryWriteRecoveryCopyAsync(config));

        var recovery = Deserialize(File.ReadAllText(temp.Config.RecoveryFile));
        var moved = recovery.Pages[0].Widgets.Single(widget => widget.Id == "media-play-pause");
        Assert.Equal(300, moved.X);
        Assert.Equal(200, moved.Y);
    }

    [Fact]
    public async Task TryWriteRecoveryCopyAsync_WithACanceledToken_IsBoundedAndWritesNothing()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var written = await store.TryWriteRecoveryCopyAsync(DefaultConfigGenerator.Create(), cts.Token);

        Assert.False(written);
        Assert.False(File.Exists(temp.Config.RecoveryFile));
        Assert.False(File.Exists(temp.Config.RecoveryFile + ".tmp"));
    }

    [Fact]
    public async Task TryWriteRecoveryCopyAsync_WhenTheTargetIsLocked_ReturnsFalseWithoutThrowing()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        File.WriteAllText(temp.Config.RecoveryFile, "locked");

        bool written;
        using (new FileStream(temp.Config.RecoveryFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            written = await store.TryWriteRecoveryCopyAsync(DefaultConfigGenerator.Create());
        }

        Assert.False(written);
        Assert.Equal("locked", File.ReadAllText(temp.Config.RecoveryFile));
        Assert.False(File.Exists(temp.Config.RecoveryFile + ".tmp"));
    }

    [Fact]
    public async Task TryWriteRecoveryCopyAsync_RefusesASchemaVersionThisBuildDoesNotOwn()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        var future = DefaultConfigGenerator.Create();
        future.SchemaVersion = PanelConfig.CurrentSchemaVersion + 1;

        var written = await store.TryWriteRecoveryCopyAsync(future);

        Assert.False(written);
        Assert.False(File.Exists(temp.Config.RecoveryFile));
    }

    private static JsonConfigStore CreateStore(TempDirectory temp) =>
        new(temp.Config, migrations: null, timeProvider: null, logger: CreateLogger());

    private static ILogger CreateLogger() =>
        new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new RecordingSink())
            .CreateLogger();

    private static PanelConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
}
