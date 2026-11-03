using Skopka.Hello.Admin;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloAdminClientLocalizationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void ClientPageKeysExistInEachCultureWithoutFallback(string culture)
    {
        var catalog = new HelloUiTextCatalog(
            new SkopkaHelloUiOptions(),
            [
                new HelloUiDictionarySource(typeof(HelloUiModule).Assembly,
                    ["Skopka.Hello.UI.Localization.en.json", "Skopka.Hello.UI.Localization.ru.json"]),
                new HelloUiDictionarySource(typeof(HelloAdminDefaults).Assembly,
                    ["Skopka.Hello.Admin.Localization.en.json", "Skopka.Hello.Admin.Localization.ru.json"]),
            ]);
        var texts = catalog.GetAllStrings(culture, includeParentCultures: false);
        string[] keys =
        [
            "Title", "SecretOnce", "Verify", "Code", "Confirm", "Manage", "Action",
            "Create", "Update", "Delete", "Rotate", "Name", "Revision", "Type",
            "Resource", "Scopes", "Redirects", "LogoutRedirects", "Reauthentication",
            "SendCode", "Pages", "Previous", "Next", "Managed", "ReadOnly", "Completed",
            "EditTitle", "CreateTitle", "NewClient", "Cancel",
            "ClientId", "Public", "Confidential", "Yes", "No",
        ];

        foreach (var key in keys)
        {
            AssertLocalized("Admin.Clients." + key);
        }

        string[] layoutKeys =
        [
            "Admin.Common.Administration", "Admin.Users.Title", "Admin.Roles.Title",
            "Layout.ReturnToApplication", "Layout.Account", "Layout.SignOut",
            "Layout.Language", "Layout.ChangeLanguage",
        ];
        foreach (var key in layoutKeys)
        {
            AssertLocalized(key);
        }

        void AssertLocalized(string key)
        {
            Assert.True(texts.TryGetValue(key, out var value), $"Missing {culture}: {key}");
            Assert.False(string.IsNullOrWhiteSpace(value), $"Empty {culture}: {key}");
            Assert.NotEqual(key, value);
        }
    }
}
