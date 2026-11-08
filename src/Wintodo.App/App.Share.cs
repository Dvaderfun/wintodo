using System.Diagnostics;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Wintodo.Core.Models;
using Wintodo.Core.Presentation;
using Wintodo.Core.Services;
using Wintodo.Data;

namespace Wintodo;

public partial class App
{
    private static async Task HandleShareAsync(IShareTargetActivatedEventArgs args)
    {
        Trace.WriteLine("WINTODO_SHARE_STAGE=window-creating");
        var resources = new ResourceLoader();
        var window = new ShareWindow();
        Window = window;
        window.Activate();
        Trace.WriteLine("WINTODO_SHARE_STAGE=window-activated");
        var operation = args.ShareOperation;
        bool committed = false;
        string? error = null;
        try
        {
            // ShareOperation is apartment-bound. Every await resumes on this UI dispatcher.
            operation.ReportStarted();
            Trace.WriteLine("WINTODO_SHARE_STAGE=started");
            string? text = operation.Data.Contains(StandardDataFormats.Text)
                ? await operation.Data.GetTextAsync() : null;
            string? link = operation.Data.Contains(StandardDataFormats.WebLink)
                ? (await operation.Data.GetWebLinkAsync()).OriginalString : null;
            Trace.WriteLine("WINTODO_SHARE_STAGE=data-retrieved");
            if (!SharedTaskContent.TryMap(text, link, operation.Data.Properties.Title, out var content))
            {
                error = resources.GetString("Share_Unsupported");
            }
            else
            {
                operation.ReportDataRetrieved();
                // Broker shares always use package LocalState; no release scratch override.
                await using var store = new SqliteTaskStore(
                    Path.Combine(ApplicationData.Current.LocalFolder.Path, "wintodo.db"), new SystemClock());
                await store.InitializeAsync();
                Trace.WriteLine("WINTODO_SHARE_STAGE=store-initialized");
                TodoList list = await store.EnsureDefaultListAsync(resources.GetString("DefaultList_Name"));
                await store.CreateTaskAsync(new TodoTask
                {
                    ListId = list.Id, Summary = content!.Title, Description = content.Note,
                });
                committed = true;
                Trace.WriteLine("WINTODO_SHARE_STAGE=committed");
                operation.ReportCompleted();
                Trace.WriteLine("WINTODO_SHARE_STAGE=completed");
            }
        }
        catch (UnsupportedDatabaseSchemaException)
        {
            Trace.WriteLine("WINTODO_SHARE_DATABASE_SCHEMA_UNSUPPORTED");
            error = resources.GetString("Database_NewerSchemaMessage");
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WINTODO_SHARE_TARGET_EXCEPTION={DiagnosticException.Describe(exception)};COMMITTED={committed}");
            error = resources.GetString(committed ? "Share_CompletionFailed" : "Share_CreateFailed");
        }
        if (error is null)
        {
            window.Complete();
            return;
        }
        // Report failures once and keep a closeable localized error surface; never retry a save.
        try { operation.ReportError(error); }
        catch (Exception exception) { Trace.WriteLine($"WINTODO_SHARE_REPORT_EXCEPTION={DiagnosticException.Describe(exception)}"); }
        window.ShowError(error);
    }
}
