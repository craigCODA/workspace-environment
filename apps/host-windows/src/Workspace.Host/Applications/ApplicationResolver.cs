using System.Text;

namespace Workspace.Host.Applications;

public enum ApplicationResolutionStatus
{
    NotFound,
    Resolved,
    Ambiguous,
}

public sealed record ApplicationResolution(
    ApplicationResolutionStatus Status,
    ApplicationDescriptor? Application,
    IReadOnlyList<ApplicationDescriptor> Candidates);

public static class ApplicationResolver
{
    public static ApplicationResolution Resolve(
        string query,
        IEnumerable<ApplicationDescriptor> applications)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(applications);

        var normalizedQuery = Normalize(query);
        var candidates = applications
            .Where(application => application is not null)
            .ToArray();

        return ResolveMatches(candidates.Where(application =>
            EqualsNormalized(application.Id, normalizedQuery)))
            ?? ResolveMatches(candidates.Where(application =>
                SearchTerms(application).Any(term => EqualsNormalized(term, normalizedQuery))))
            ?? ResolveMatches(candidates.Where(application =>
                SearchTerms(application).Any(term => Normalize(term).StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))))
            ?? ResolveMatches(candidates.Where(application =>
                Tokens(application).Contains(normalizedQuery, StringComparer.OrdinalIgnoreCase)))
            ?? NotFound();
    }

    private static ApplicationResolution? ResolveMatches(IEnumerable<ApplicationDescriptor> matches)
    {
        var ordered = matches
            .DistinctBy(application => application.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ordered.Length switch
        {
            0 => null,
            1 => new ApplicationResolution(ApplicationResolutionStatus.Resolved, ordered[0], Array.Empty<ApplicationDescriptor>()),
            _ => new ApplicationResolution(ApplicationResolutionStatus.Ambiguous, null, ordered),
        };
    }

    private static ApplicationResolution NotFound() =>
        new(ApplicationResolutionStatus.NotFound, null, Array.Empty<ApplicationDescriptor>());

    private static IEnumerable<string> SearchTerms(ApplicationDescriptor application)
    {
        yield return application.DisplayName;

        foreach (var alias in application.Aliases ?? Array.Empty<string>())
        {
            yield return alias;
        }
    }

    private static IEnumerable<string> Tokens(ApplicationDescriptor application)
    {
        foreach (var term in SearchTerms(application))
        {
            var normalized = Normalize(term);
            var token = new StringBuilder();

            foreach (var character in normalized)
            {
                if (char.IsLetterOrDigit(character))
                {
                    token.Append(character);
                }
                else if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
            }

            if (token.Length > 0)
            {
                yield return token.ToString();
            }
        }
    }

    private static bool EqualsNormalized(string value, string normalizedQuery) =>
        string.Equals(Normalize(value), normalizedQuery, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(normalized.Length);
        var whitespacePending = false;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                whitespacePending = result.Length > 0;
                continue;
            }

            if (whitespacePending)
            {
                result.Append(' ');
                whitespacePending = false;
            }

            result.Append(character);
        }

        return result.ToString();
    }
}
