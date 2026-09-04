using Archipelago.MultiClient.Net.Enums;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Which login refusals are worth retrying.
///
/// The server distinguishes these and the client was throwing the distinction
/// away: every failure went into the same backoff, so a typo in the slot name
/// was retried three times over nine seconds before the player was told
/// anything - and the thing being retried could never have succeeded.
///
/// Here rather than beside the socket because it is a pure decision over an
/// enum the protocol defines, and it is the kind of list that gets a new member
/// added carelessly. A test is cheaper than rediscovering the rule.
/// </summary>
public static class Refusals
{
    /// <summary>
    /// True when retrying cannot help and the player has to change something.
    ///
    /// SlotAlreadyTaken is deliberately absent. The usual cause is an earlier
    /// socket of ours that has not timed out yet, and waiting is exactly what
    /// helps - so it is treated as transient.
    ///
    /// UnknownError is absent too, and for the opposite reason: the library
    /// returns it for a code it does not recognise, which is a reason to be
    /// cautious rather than a reason to conclude anything. Retrying an unknown
    /// refusal a few times costs seconds; refusing to retry a transient one
    /// strands the player offline.
    /// </summary>
    public static bool IsTerminal(IEnumerable<ConnectionRefusedError>? codes)
    {
        if (codes == null) return false;

        foreach (var code in codes)
        {
            switch (code)
            {
                case ConnectionRefusedError.InvalidSlot:
                case ConnectionRefusedError.InvalidGame:
                case ConnectionRefusedError.InvalidPassword:
                case ConnectionRefusedError.IncompatibleVersion:
                case ConnectionRefusedError.InvalidItemsHandling:
                    return true;
            }
        }
        return false;
    }
}
