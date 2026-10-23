using Swip.Shared;

namespace Swip.Tests;

public class MenuCornersTests
{
    [Theory]
    [InlineData(22000)]   // Windows 11 21H2
    [InlineData(22631)]   // 23H2
    [InlineData(26100)]   // 24H2
    public void En_Windows_11_con_desenfoque_redondea_DWM(int build)
    {
        Assert.Equal(MenuCornerMode.Dwm, MenuCorners.Choose(build, blurEnabled: true));
    }

    [Theory]
    [InlineData(19045)]   // Windows 10 22H2
    [InlineData(21999)]
    public void En_Windows_10_se_usa_el_respaldo(int build)
    {
        Assert.Equal(MenuCornerMode.Layered, MenuCorners.Choose(build, blurEnabled: true));
    }

    [Fact]
    public void Sin_desenfoque_se_usa_la_ventana_transparente_de_siempre()
    {
        Assert.Equal(MenuCornerMode.Layered, MenuCorners.Choose(26100, blurEnabled: false));
    }
}
