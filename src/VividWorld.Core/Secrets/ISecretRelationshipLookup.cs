using System.Collections.Generic;

namespace VividWorld.Core.Secrets
{
    public interface ISecretRelationshipLookup
    {
        IReadOnlyCollection<string> GetKin(string heroId);
        IReadOnlyCollection<string> GetFriends(string heroId);
        int? GetAffection(string heroA, string heroB);
        bool IsKin(string heroA, string heroB);
    }
}
