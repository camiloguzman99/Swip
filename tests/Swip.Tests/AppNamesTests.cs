using Swip.Shared;

namespace Swip.Tests;

public class AppNamesTests
{
    [Theory]
    [InlineData("WhatsApp.Root", "WhatsApp")]     // el caso real
    [InlineData("whatsapp.root", "whatsapp")]     // sin distinguir mayúsculas
    [InlineData("WhatsApp.ROOT", "WhatsApp")]
    [InlineData("  WhatsApp.Root  ", "WhatsApp")] // con espacios alrededor
    [InlineData("Claude.exe", "Claude")]
    [InlineData("App.Root.exe", "App")]           // más de un sufijo
    public void Quita_los_sufijos_tecnicos(string entrada, string esperado)
    {
        Assert.Equal(esperado, AppNames.Clean(entrada));
    }

    [Theory]
    [InlineData("Microsoft Edge")]
    [InlineData("Microsoft Outlook")]
    [InlineData("Explorador de Windows")]
    [InlineData("Microsoft.Photos")]     // un punto en medio NO es un sufijo técnico
    [InlineData("Realtek Audio Console")]
    [InlineData("Rootkit Scanner")]      // contiene "root" pero no como sufijo ".Root"
    public void No_toca_los_nombres_normales(string nombre)
    {
        Assert.Equal(nombre, AppNames.Clean(nombre));
    }

    [Theory]
    [InlineData(".Root")]
    [InlineData(".exe")]
    public void Nunca_deja_el_nombre_vacio(string nombre)
    {
        Assert.Equal(nombre, AppNames.Clean(nombre));
    }
}
