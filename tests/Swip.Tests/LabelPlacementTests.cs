using Swip.Shared;

namespace Swip.Tests;

public class LabelPlacementTests
{
    private const double OneLine = 15;   // alto del rótulo de una fila
    private const double TwoLines = 27;  // alto del rótulo de dos filas

    private static double CenterOf(double top, double height) => top + height / 2;

    // La condición pedida: misma distancia desde el CENTRO del cuadro de texto para todos los gatos.
    [Theory]
    [InlineData(OneLine)]
    [InlineData(TwoLines)]
    [InlineData(40)]
    public void El_centro_del_rotulo_queda_siempre_a_la_misma_distancia_del_gato(double alto)
    {
        const double cabeza = 30;

        double top = LabelPlacement.Top(cabeza, alto);

        Assert.Equal(LabelPlacement.CenterDistance, cabeza - CenterOf(top, alto), 9);
    }

    [Fact]
    public void Un_nombre_de_una_fila_y_otro_de_dos_comparten_la_distancia_al_centro()
    {
        double uno = CenterOf(LabelPlacement.Top(12, OneLine), OneLine);
        double dos = CenterOf(LabelPlacement.Top(12, TwoLines), TwoLines);

        Assert.Equal(uno, dos, 9);   // mismo centro: ya no depende de cuántas filas tenga
    }

    [Theory]
    [InlineData(0)]     // gato "cargado": lo más alto está en el borde del control
    [InlineData(12)]    // caminando
    [InlineData(29)]    // dormido: la silueta empieza muy abajo
    public void La_distancia_se_mantiene_con_cualquier_pose(double cabeza)
    {
        double top = LabelPlacement.Top(cabeza, TwoLines);

        Assert.Equal(LabelPlacement.CenterDistance, cabeza - CenterOf(top, TwoLines), 9);
    }

    // Que no toque al gato: el borde inferior debe quedar bien separado, incluso con dos filas.
    [Theory]
    [InlineData(OneLine)]
    [InlineData(TwoLines)]
    public void El_rotulo_no_toca_al_gato(double alto)
    {
        const double cabeza = 12;

        double bordeInferior = LabelPlacement.Top(cabeza, alto) + alto;

        Assert.True(cabeza - bordeInferior >= 8,
            $"el borde inferior debe quedar al menos 8 px sobre el gato (queda a {cabeza - bordeInferior:0.#})");
    }

    [Fact]
    public void Mas_distancia_sube_el_rotulo()
    {
        double cerca = LabelPlacement.Top(12, TwoLines, centerDistance: 20);
        double lejos = LabelPlacement.Top(12, TwoLines, centerDistance: 30);

        Assert.True(lejos < cerca);   // Y menor = más arriba
    }

    [Fact]
    public void Esta_mas_arriba_que_antes()
    {
        // Antes: borde inferior a 5 px de la cabeza. Con dos filas eso era un centro a 18,5 px.
        const double antes = 5 + TwoLines / 2;

        Assert.True(LabelPlacement.CenterDistance > antes);
    }
}
