using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlexWoW.Web.Tests;

/// <summary>Загрузка карты displayid→иконка и фоллбэк-заглушка (без сети, временный wwwroot).</summary>
public sealed class ItemIconServiceTests
{
    [Fact]
    public void Resolves_known_displayid_and_falls_back_for_unknown()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "icons"));
            File.WriteAllText(Path.Combine(root, "icons", "_map.tsv"),
                "9913\tinv_pants_02\n242\tinv_boots_01\n");

            var svc = new ItemIconService(new FakeEnv(root), NullLogger<ItemIconService>.Instance);

            Assert.Equal("/icons/inv_pants_02.png", svc.IconUrl(9913));
            Assert.Equal("/icons/inv_boots_01.png", svc.IconUrl(242));
            Assert.Equal($"/icons/{ItemIconService.FallbackIcon}.png", svc.IconUrl(999999)); // нет в карте
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_map_file_falls_back_for_everything()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var svc = new ItemIconService(new FakeEnv(root), NullLogger<ItemIconService>.Instance);
            Assert.Equal($"/icons/{ItemIconService.FallbackIcon}.png", svc.IconUrl(9913));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeEnv(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = webRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string EnvironmentName { get; set; } = "Test";
    }
}
