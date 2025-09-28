// Program.cs (WebMigratorUI)
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
// Alias the console tool's Program to avoid clashing with the web app's Program
using Migrator = ASP_dotnet_MigrationTool.Program;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();

// Explicit conventional route for MVC controllers
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Upload}/{action=Index}/{id?}"
);

// Optional convenience redirect
app.MapGet("/", () => Results.Redirect("/Upload"));

app.Run();

public class UploadController : Controller
{
    [HttpGet("/Upload")]
    public IActionResult Index() => View();

    [HttpPost("/Upload")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<IActionResult> DoUpload(IFormFile legacyZip, string @namespace)
    {
        if (legacyZip is null || legacyZip.Length == 0)
            return BadRequest("Please upload a legacy .zip");

        if (string.IsNullOrWhiteSpace(@namespace))
            @namespace = "SampleApp.ViewModels";

        var tempRoot = Path.Combine(Path.GetTempPath(), "wf2dotvvm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        // Save uploaded zip
        var zipPath = Path.Combine(tempRoot, "legacy.zip");
        using (var fs = System.IO.File.Create(zipPath))
        {
            await legacyZip.CopyToAsync(fs);
        }

        // Extract
        var legacyRoot = Path.Combine(tempRoot, "legacy");
        Directory.CreateDirectory(legacyRoot);
        ZipFile.ExtractToDirectory(zipPath, legacyRoot);

        // Run migration into /out
        var outDir = Path.Combine(tempRoot, "out");
        Migrator.MigrateFolder(legacyRoot, outDir, @namespace);

        // Zip the output for download
        var outZip = Path.Combine(tempRoot, "DotVVM_Migrated.zip");
        if (System.IO.File.Exists(outZip)) System.IO.File.Delete(outZip);
        ZipFile.CreateFromDirectory(outDir, outZip);

        // Stream back the result
        var stream = System.IO.File.OpenRead(outZip);
        return File(stream, "application/zip", "DotVVM_Migrated.zip");
    }
}
