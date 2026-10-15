using PcCheck.Core.Models;

namespace PcCheck.Core.Analysis;

public enum HealthGrade
{
    Excellent,
    Good,
    Fair,
    Poor,
}

/// <summary>Điểm sức khỏe 0-100: trừ điểm theo mức độ, có giới hạn mỗi nhóm để một nhóm không kéo điểm về 0.</summary>
public static class HealthScore
{
    private const double CriticalPenalty = 15;
    private const double WarningPenalty = 6;
    private const double InfoPenalty = 1;
    private const double CategoryCap = 30;

    public static int Compute(IEnumerable<Issue> issues)
    {
        double total = 0;
        foreach (var group in issues.GroupBy(i => i.Category))
        {
            var penalty = group.Sum(i => i.Severity switch
            {
                Severity.Critical => CriticalPenalty,
                Severity.Warning => WarningPenalty,
                Severity.Info => InfoPenalty,
                _ => 0,
            });
            total += Math.Min(penalty, CategoryCap);
        }

        return (int)Math.Round(Math.Clamp(100 - total, 0, 100));
    }

    public static HealthGrade Grade(int score) => score switch
    {
        >= 90 => HealthGrade.Excellent,
        >= 75 => HealthGrade.Good,
        >= 50 => HealthGrade.Fair,
        _ => HealthGrade.Poor,
    };
}
