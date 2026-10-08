using System.Globalization;

namespace Swip.Shared;

/// <summary>
/// Formato de los porcentajes de CPU y RAM del menú. Con enteros, una app que usa el 0,4% de la RAM
/// salía como "0%" y parecía que el dato era falso; por debajo del 10% se muestra un decimal.
/// </summary>
public static class PercentFormat
{
    public static string Format(double percent, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (double.IsNaN(percent) || percent <= 0) return "0%";

        // Se decide sobre el valor YA redondeado: 9,96 se muestra como "10%", no como "10,0%".
        double oneDecimal = Math.Round(percent, 1, MidpointRounding.AwayFromZero);
        if (oneDecimal <= 0) return "0%";
        if (oneDecimal < 10) return oneDecimal.ToString("0.0", culture) + "%";
        return Math.Min(Math.Round(percent, MidpointRounding.AwayFromZero), 100).ToString("0", culture) + "%";
    }
}
