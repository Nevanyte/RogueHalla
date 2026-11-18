using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Roguehalla.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Roguehalla.Client
{
    /// <summary>
    /// Purchase and equip requests: the latest mutation per item, in-flight checks, retry, sending to the account API, and the player-facing text for mutations and failures.
    /// </summary>
    public sealed partial class HubCosmeticsView
    {
        MutationEntry FindLatest(AccountCosmeticDefinition item, AccountMutationKind kind)
        {
            AccountApiService api = _owner != null ? _owner.Api : null;
            if (api == null) return null;
            string slot = kind == AccountMutationKind.Equip ? item.Slot : null;
            for (int i = _mutations.Count - 1; i >= 0; i--)
            {
                var operation = _mutations[i].Operation;
                bool sameItem = kind == AccountMutationKind.Purchase ? operation.ItemId == item.Id :
                    operation.ItemId == item.Id || (operation.ItemId == null && _equipped.TryGetValue(item.Slot, out var active) && active == item.Id);
                if (operation.Kind == kind && operation.Slot == slot && sameItem &&
                    SameOwner(operation.Name, _observedName) && api.IsCurrentOwner(operation))
                    return _mutations[i];
            }
            return null;
        }

        bool HasCurrentOwnerMutationInFlight(string ownerName)
        {
            AccountApiService api = _owner != null ? _owner.Api : null;
            if (api == null) return false;
            for (int i = 0; i < _mutations.Count; i++)
            {
                AccountMutation operation = _mutations[i].Operation;
                if ((_mutations[i].Submitting || operation.State == AccountMutationState.InFlight) &&
                    SameOwner(operation.Name, ownerName) && api.IsCurrentOwner(operation)) return true;
            }
            return false;
        }

        void Purchase(AccountCosmeticDefinition item)
        {
            var profile = CurrentProfile();
            if (profile == null || _owner.Api == null || _owned.Contains(item.Id) || !item.Price.HasValue || HasCurrentOwnerMutationInFlight(profile.Name)) return;
            var operation = _owner.Api.CreatePurchase(item.Id);
            var entry = new MutationEntry { Operation = operation };
            _mutations.Add(entry);
            SendMutation(entry);
        }

        void Equip(string slot, string itemId)
        {
            var profile = CurrentProfile();
            if (profile == null || _owner.Api == null || !CanSubmitEquip(slot, itemId, _owned, _equipped) || HasCurrentOwnerMutationInFlight(profile.Name)) return;
            var operation = _owner.Api.CreateEquip(slot, itemId);
            var entry = new MutationEntry { Operation = operation };
            _mutations.Add(entry);
            SendMutation(entry);
        }

        void Retry(MutationEntry entry)
        {
            var profile = CurrentProfile();
            AccountApiService api = _owner != null ? _owner.Api : null;
            if (entry == null || profile == null || api == null || entry.Operation.State != AccountMutationState.Unknown ||
                !api.IsCurrentOwner(entry.Operation) ||
                !SameOwner(entry.Operation.Name, profile.Name) || HasCurrentOwnerMutationInFlight(profile.Name)) return;
            SendMutation(entry); // Keep the original AccountMutation reference and purchase UUID.
        }

        async void SendMutation(MutationEntry entry)
        {
            var api = _owner != null ? _owner.Api : null;
            if (api == null || entry == null || !CanMutateCurrentOwner(api)) return;
            long generation = _viewGeneration;
            entry.Submitting = true;
            Task<AccountApiResult> pending;
            try { pending = api.SendAsync(entry.Operation); }
            catch
            {
                entry.Submitting = false;
                if (CanApplyView(generation, api)) RebuildCards();
                return;
            }
            if (CanApplyView(generation, api)) RebuildCards();
            AccountApiResult result;
            try { result = await pending; }
            catch
            {
                entry.Submitting = false;
                if (CanApplyView(generation, api)) RebuildCards();
                return;
            }
            entry.Submitting = false;
            // Keep a current result with its operation for later rendering, but never publish it into a stale/hidden view.
            if (!api.IsCurrent(result))
            {
                if (CanApplyView(generation, api)) RebuildCards();
                return;
            }
            entry.Result = result;
            if (!CanApplyView(generation, api)) return;
            RebuildCards();
        }

        bool CanMutateCurrentOwner(AccountApiService api)
        {
            var profile = CurrentProfile();
            return profile != null && api != null && ReferenceEquals(_owner.Api, api) &&
                (_currentRead == null || api.IsCurrent(_currentRead)) &&
                !HasCurrentOwnerMutationInFlight(profile.Name);
        }

        string MutationSummary(AccountFullProfile profile)
        {
            AccountApiService api = _owner != null ? _owner.Api : null;
            if (api == null) return null;
            for (int i = _mutations.Count - 1; i >= 0; i--)
            {
                var operation = _mutations[i].Operation;
                if (!SameOwner(operation.Name, profile.Name) || !api.IsCurrentOwner(operation)) continue;
                // An uncertain operation survives same-credential reopen; its old delivery generation does not.
                // Current operation ownership above still excludes a replacement credential's history.
                if (operation.State != AccountMutationState.Unknown &&
                    _mutations[i].Result != null && !api.IsCurrent(_mutations[i].Result)) continue;
                if (_mutations[i].Submitting) return "Account request queued or in flight · do not submit a duplicate.";
                if (operation.State == AccountMutationState.Unknown)
                    return "Confirmation was interrupted. Retry resubmits this change.";
                if (operation.State == AccountMutationState.Refused) return ErrorText(operation.Error);
                if (operation.State == AccountMutationState.InFlight) return "Waiting for confirmation…";
                if (operation.State == AccountMutationState.Confirmed) return "Saved to your profile.";
            }
            return null;
        }

        static string MutationText(AccountMutation operation)
        {
            switch (operation.State)
            {
                case AccountMutationState.Ready: return "Ready · request has not been sent.";
                case AccountMutationState.InFlight: return "Waiting for confirmation…";
                case AccountMutationState.Confirmed: return "Saved to your profile.";
                case AccountMutationState.Unknown: return "Confirmation interrupted · Retry resubmits this change.";
                case AccountMutationState.Refused: return ErrorText(operation.Error);
                default: return "No request has been sent.";
            }
        }

        static string ErrorText(AccountApiError error)
        {
            if (error == null) return "Request refused by the account service.";
            if (error.Failure == AccountApiFailure.InsufficientGv && error.Need.HasValue && error.Have.HasValue)
                return "Not enough GV · Need " + FormatGv(error.Need.Value) + " GV · Have " + FormatGv(error.Have.Value) + " GV.";
            switch (error.Failure)
            {
                case AccountApiFailure.Forbidden: return "The account service refused this request.";
                case AccountApiFailure.PasscodeRequired: return "Open the profile with its required passcode before retrying.";
                case AccountApiFailure.UnknownCosmetic: return "This item is unavailable.";
                case AccountApiFailure.UnknownSlot: return "This category is unavailable.";
                case AccountApiFailure.NotForSale: return "This cosmetic is not for sale.";
                case AccountApiFailure.AlreadyOwned: return "You already own this cosmetic.";
                case AccountApiFailure.NotOwned: return "You do not own this cosmetic.";
                case AccountApiFailure.SlotMismatch: return "This cosmetic cannot be equipped in that category.";
                case AccountApiFailure.RateLimited: return "Too many requests. Wait a moment and try again.";
                case AccountApiFailure.BadRequest: return "This change could not be submitted. Refresh your profile and try again.";
                case AccountApiFailure.Unreadable: return "The account response could not be read.";
                default: return "The account service did not confirm this request.";
            }
        }

        static string AccountFailureText(AccountApiFailure failure)
        {
            switch (failure)
            {
                case AccountApiFailure.NoOwner: return "Open an owned profile to see account cosmetics.";
                case AccountApiFailure.PasscodeRequired: return "Open the profile with its required passcode.";
                case AccountApiFailure.Forbidden: return "This profile could not be opened.";
                case AccountApiFailure.BadRequest: return "The selected profile details are invalid.";
                case AccountApiFailure.RateLimited: return "Too many requests. Wait a moment and try again.";
                case AccountApiFailure.Unreadable: return "A complete account profile was not available.";
                default: return "Profile refresh failed. Try again when the account service is available.";
            }
        }
    }
}
