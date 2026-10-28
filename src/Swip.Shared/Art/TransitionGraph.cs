namespace Swip.Shared.Art;

/// <summary>Una transición dibujada entre dos acciones básicas: el clip que las une (reproducido tal cual o al revés).</summary>
public sealed record TransitionEdge(string Clip, string From, string To, bool Reverse);

/// <summary>
/// El grafo de acciones básicas del Visor: qué clips llevan de una acción a otra. Al elegir otra acción, el Visor
/// reproduce el camino de transiciones más corto desde la actual y termina en la elegida. Los pares que se pueden
/// enlazar con un corte directo (la acción de destino ya arranca desde la pose de origen) no añaden ningún clip.
/// </summary>
public sealed class TransitionGraph
{
    private const double TransitionCost = 1, DirectCost = 0.01;

    private readonly List<TransitionEdge> _transitions;
    private readonly List<(string From, string To)> _direct;

    public TransitionGraph(IEnumerable<TransitionEdge> transitions, IEnumerable<(string From, string To)> direct)
    {
        _transitions = transitions.ToList();
        _direct = direct.ToList();
    }

    /// <summary>
    /// Transiciones (en orden) que llevan de <paramref name="from"/> a <paramref name="to"/>; lista vacía si ya
    /// está en esa acción o si basta un corte directo; null si no hay camino.
    /// </summary>
    public IReadOnlyList<TransitionEdge>? FindPath(string from, string to)
    {
        if (from == to) return Array.Empty<TransitionEdge>();
        var cost = new Dictionary<string, double> { [from] = 0 };
        var via = new Dictionary<string, (string Prev, TransitionEdge? Edge)>();
        var open = new List<string> { from };
        while (open.Count > 0)
        {
            string node = open.OrderBy(n => cost[n]).First();
            open.Remove(node);
            if (node == to) break;
            var edges = _transitions.Where(t => t.From == node).Select(t => (To: t.To, Edge: (TransitionEdge?)t, Cost: TransitionCost))
                .Concat(_direct.Where(d => d.From == node).Select(d => (To: d.To, Edge: (TransitionEdge?)null, Cost: DirectCost)));
            foreach (var (next, edge, c) in edges)
            {
                double total = cost[node] + c;
                if (cost.TryGetValue(next, out double known) && known <= total) continue;
                cost[next] = total;
                via[next] = (node, edge);
                if (!open.Contains(next)) open.Add(next);
            }
        }
        if (!via.ContainsKey(to)) return null;
        var path = new List<TransitionEdge>();
        for (string cur = to; cur != from; cur = via[cur].Prev)
            if (via[cur].Edge is { } edge) path.Insert(0, edge);
        return path;
    }
}
