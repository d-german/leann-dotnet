using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services;

public interface IPassageStore
{
    Result<PassageData> GetPassage(string passageId);
    int Count { get; }
    IEnumerable<PassageData> EnumerateAll();
}
