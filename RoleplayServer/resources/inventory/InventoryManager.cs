﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using GTANetworkServer;
using GTANetworkShared;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using RoleplayServer.resources.core;
using RoleplayServer.resources.inventory.bags;
using RoleplayServer.resources.player_manager;

namespace RoleplayServer.resources.inventory
{
    class InventoryManager : Script
    {
        public InventoryManager()
        {
            _activeInvsBeingManaged = new Dictionary<Client, KeyValuePair<IStorage, IStorage>>();

            #region Inventory Items

            BsonClassMap.RegisterClassMap<BagItem>();
            BsonClassMap.RegisterClassMap<TestItem>();

            #endregion


            API.onClientEventTrigger += API_onClientEventTrigger;
        }

        public enum GiveItemErrors
        {
            NotEnoughSpace,
            HasBlockingItem,
            MaxAmountReached,
            Success
        }
        private static IInventoryItem CloneItem(IInventoryItem item, int amount = -1)
        {
            var type = item.GetType();
            var properties = type.GetProperties();
            var newObject = ItemTypeToNewObject(type);
            foreach (var prop in properties)
            {
                if(prop.CanWrite)
                    prop.SetValue(newObject, prop.GetValue(item));
            }
            if (amount != -1) newObject.Amount = amount;
            return newObject;
        }


        /// <summary>
        /// Gives player an item.
        /// NOTE: The item object is cloned, so the actual object passed isn't referenced.
        /// Use DeleteInventoryItem to take.
        /// </summary>
        /// <param name="storage">The storage you wanna add to.</param>
        /// <param name="olditem">The item object, the Amount inside is ignored.</param>
        /// <param name="amount">The amount of this item to be added, item will be duplicated if not stackable.</param>
        /// <param name="ignoreBlocking">Add even if inventory is blocked due to big item.</param>
        /// <returns></returns>
        public static GiveItemErrors GiveInventoryItem(IStorage storage, IInventoryItem olditem, int amount = 1, bool ignoreBlocking = false)
        {
            //We wanna clone and add it.
            var item = CloneItem(olditem, amount);

            if (storage.Inventory == null) storage.Inventory = new List<IInventoryItem>();
            //Make sure he doesn't have blocking item.
            if(storage.Inventory.FirstOrDefault(x => x.IsBlocking == true) != null && ignoreBlocking == false)
                return GiveItemErrors.HasBlockingItem;

            //Check if player has simliar item.
            var oldItem = storage.Inventory.FirstOrDefault(x => x.GetType() == item.GetType());
            if (oldItem == null || oldItem.CanBeStacked == false)
            {
                if (item.MaxAmount != -1 && oldItem?.Amount >= item.MaxAmount)
                {
                    return GiveItemErrors.MaxAmountReached;
                }
                //Check if has enough space.
                if ((GetInventoryFilledSlots(storage) + item.Amount * item.AmountOfSlots) <= storage.MaxInvStorage)
                {
                    //Set an id.
                    if(item.Id == ObjectId.Empty) ObjectId.GenerateNewId(DateTime.Now);

                    //Add.
                    storage.Inventory.Add(item);
                    return GiveItemErrors.Success;
                }
                else
                    return GiveItemErrors.NotEnoughSpace;
            }
            else
            {
                if (item.MaxAmount != -1 && oldItem.Amount >= item.MaxAmount)
                {
                    return GiveItemErrors.MaxAmountReached;
                }

                //Make sure there is space again.
                if ((GetInventoryFilledSlots(storage) + item.Amount * item.AmountOfSlots) <= storage.MaxInvStorage)
                {
                    //Add.
                    oldItem.Amount += item.Amount;
                    if (oldItem.Amount == 0)
                    {
                        storage.Inventory.Remove(oldItem);
                    }
                    return GiveItemErrors.Success;
                }
                else
                    return GiveItemErrors.NotEnoughSpace;
            }
        }

        /// <summary>
        /// Checks if user has certain item.
        /// </summary>
        /// <param name="storage">The storage that shall be checked.</param>
        /// <param name="item">The item Type</param>
        /// <returns>An array of IInventoryItem.</returns>
        public static IInventoryItem[] DoesInventoryHaveItem(IStorage storage, Type item)
        {
            if (storage.Inventory == null) storage.Inventory = new List<IInventoryItem>();
            return storage.Inventory.Where(x => x.GetType() == item).ToArray();
        }


        /// <summary>
        /// Converts a string to its equivelent Type of IInventoryItem.
        /// </summary>
        /// <param name="item">The item string</param>
        /// <returns>The IInventoryItem Type</returns>
        public static Type ParseInventoryItem(string item)
        {
            var allItems =
                Assembly.GetExecutingAssembly().GetTypes().Where(x => typeof(IInventoryItem).IsAssignableFrom(x) && x.IsClass).ToArray();

            foreach (var i in allItems)
            {
                IInventoryItem instance = (IInventoryItem)Activator.CreateInstance(i);
                if (instance.CommandFriendlyName == item)
                    return i;
            }
            return null;
        }

        /// <summary>
        /// Gets current filled slots.
        /// </summary>
        /// <param name="storage">The storage to check.</param>
        /// <returns>An integer of sum of taken slots.</returns>
        public static int GetInventoryFilledSlots(IStorage storage)
        {
            if (storage.Inventory == null) storage.Inventory = new List<IInventoryItem>();
            int value = 0;
            storage.Inventory.ForEach(x => value += x.AmountOfSlots * x.Amount);
            return value;
        }

        /// <summary>
        /// Removes an item from storage.
        /// </summary>
        /// <param name="storage">The storage to remove from.</param>
        /// <param name="item">The item to remove.</param>
        /// <param name="amount">Amount to be removed, -1 for all.</param>
        /// <param name="predicate">The predicate to be used, can be null for none</param>
        /// <returns>true if something was removed and false if nothing was removed.</returns>
        public static bool DeleteInventoryItem(IStorage storage, Type item, int amount = -1, Func<IInventoryItem, bool> predicate = null)
        {
            if (storage.Inventory == null) storage.Inventory = new List<IInventoryItem>();
            if (amount == -1)
            {
                return storage.Inventory.RemoveAll(x => x.GetType() == item) > 0;
            }

            IInventoryItem itm = predicate != null ? storage.Inventory.Where(x => x.GetType() == item).SingleOrDefault(predicate) : storage.Inventory.SingleOrDefault(x => x.GetType() == item);
            if (itm != null)
            {
                itm.Amount -= amount;
                if (itm.Amount <= 0)
                    storage.Inventory.Remove(itm);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Creates a new object from Type.
        /// </summary>
        /// <param name="item">Item Type</param>
        /// <returns>Object of Type</returns>
        public static IInventoryItem ItemTypeToNewObject(Type item)
        {
            return (IInventoryItem)Activator.CreateInstance(item);
        }

        #region InventoryMovingManagement

        private static Dictionary<Client, KeyValuePair<IStorage, IStorage>> _activeInvsBeingManaged;
        public static void ShowInventoryManager(Client player, IStorage activeLeft, IStorage activeRight)
        {
            if (_activeInvsBeingManaged.ContainsKey(player))
            {
                API.shared.sendNotificationToPlayer(player, "You already have an inventory management window open.");
                return;
            }

            string[][] leftItems =
                activeLeft.Inventory.Where(x => x.GetType() != typeof(BagItem))
                    .Select(x => new[] {x.Id.ToString(), x.LongName, x.CommandFriendlyName, x.Amount.ToString()})
                    .ToArray();

            string[][] rightItems =
                activeRight.Inventory.Where(x => x.GetType() != typeof(BagItem))
                    .Select(x => new[] {x.Id.ToString(), x.LongName, x.CommandFriendlyName, x.Amount.ToString()})
                    .ToArray();

            var leftJson = API.shared.toJson(leftItems);
            var rightJson = API.shared.toJson(rightItems);
            API.shared.triggerClientEvent(player, "bag_showmanager", leftJson, rightJson);
            _activeInvsBeingManaged.Add(player, new KeyValuePair<IStorage, IStorage>(activeLeft, activeRight));
        }

        private void API_onClientEventTrigger(Client sender, string eventName, params object[] arguments)
        {
            switch (eventName)
            {
                case "invmanagement_cancelled":
                    _activeInvsBeingManaged.Remove(sender);
                    API.sendNotificationToPlayer(sender, "Cancelled Inventory Management.");
                    break;
                   
                case "bag_moveFromLeftToRight":
                    string id = (string)arguments[0];
                    string shortname = (string)arguments[1];
                    int amount;
                    if (!int.TryParse((string)arguments[2], out amount))
                    {
                        API.sendChatMessageToPlayer(sender, "Invalid amount entered.");
                        return;
                    }
                    if (amount <= 0)
                    {
                        API.sendChatMessageToPlayer(sender, "Amount must not be zero or negative.");
                        return;
                    }

                    //Make sure is managing.
                    if (!_activeInvsBeingManaged.ContainsKey(sender))
                    {
                        API.sendNotificationToPlayer(sender, "You aren't managing any inventory.");
                        return;
                    }

                    //Get the invs.
                    KeyValuePair<IStorage, IStorage> storages = _activeInvsBeingManaged.Get(sender);

                    //See if has item.
                    var itemType = InventoryManager.ParseInventoryItem(shortname);
                    if (itemType == null)
                    {
                        API.sendNotificationToPlayer(sender, "That item type doesn't exist.");
                        return;
                    }
                    var playerItem = InventoryManager.DoesInventoryHaveItem(storages.Key, itemType).SingleOrDefault(x => x.Id.ToString() == id);
                    if (playerItem == null || playerItem.Amount < amount)
                    {
                        API.sendNotificationToPlayer(sender, "You don't have that item or don't have that amount.");
                        return;
                    }

                    //Add to bag.
                    switch (InventoryManager.GiveInventoryItem(storages.Value, playerItem, amount, true))
                    {
                        case InventoryManager.GiveItemErrors.NotEnoughSpace:
                            API.sendNotificationToPlayer(sender, "You don't have enough slots in the target storage.");
                            break;
                        case InventoryManager.GiveItemErrors.MaxAmountReached:
                            API.sendNotificationToPlayer(sender, "Reached max amount of that item in the target storage.");
                            break;
                        case InventoryManager.GiveItemErrors.Success:
                            //Remove from player.
                            InventoryManager.DeleteInventoryItem(storages.Key, itemType, amount,
                                x => x.Id.ToString() == id && x.CommandFriendlyName == shortname);

                            //Send event done.
                            API.triggerClientEvent(sender, "moveItemFromLeftToRightSucess", id, shortname, amount); //Id should be same cause it was already set since it was in player inv.
                            API.sendNotificationToPlayer(sender, $"The item ~g~{shortname}~w~ was moved sucessfully.");
                            break;
                    }
                    break;
            }
        }

        #endregion

        [Command("give")]
        public void give_cmd(Client player, string id, string item, int amount)
        {
            var targetClient = PlayerManager.ParseClient(id);
            if (targetClient == null)
            {
                API.sendNotificationToPlayer(player, "Target player not found.");
                return;
            }
            Character sender = player.GetCharacter();
            Character target = targetClient.GetCharacter();
            if (player.position.DistanceTo(targetClient.position) > 5f)
            {
                API.sendNotificationToPlayer(player, "You must be near the target player to give him an item.");
                return;
            }

            //Get the item.
            var itemType = ParseInventoryItem(item);
            if (itemType == null)
            {
                API.sendNotificationToPlayer(player, "That item doesn't exist.");
                return;
            }
            var itemObj = ItemTypeToNewObject(itemType);
            if (itemObj.CanBeGiven == false)
            {
                API.sendNotificationToPlayer(player, "That item cannot be given.");
                return;
            }

            //Make sure he does have such amount.
            var sendersItem = DoesInventoryHaveItem(sender, itemType);
            if (sendersItem.Length != 1 || sendersItem[0].Amount < amount)
            {
                API.sendNotificationToPlayer(player, "You don't have that item or you don't have that amount or there is more than 1 item with that name.");
                return;
            }

            //Give.
            switch (GiveInventoryItem(target, sendersItem[0], amount))
            {
                case GiveItemErrors.NotEnoughSpace:
                    API.sendNotificationToPlayer(player, "The target player doesn't have enough space in his inventory.");
                    API.sendNotificationToPlayer(targetClient,
                        "Someone has tried to give you an item but failed due to insufficient inventory.");
                    break;

                case GiveItemErrors.HasBlockingItem:
                    API.sendNotificationToPlayer(player, "The target player has a blocking item in hand.");
                    API.sendNotificationToPlayer(targetClient,
                        "You have a blocking item in-hand, place it somewhere first. /inv to find out what it is.");
                    break;

                case GiveItemErrors.Success:
                    API.sendNotificationToPlayer(player,
                        $"You have sucessfully given ~g~{amount}~w~ ~g~{sendersItem[0].LongName}~w~ to ~g~{target.CharacterName}~w~.");
                    API.sendNotificationToPlayer(targetClient,
                        $"You have receieved ~g~{amount}~w~ ~g~{sendersItem[0].LongName}~w~ from ~g~{sender.CharacterName}~w~.");

                    //Remove from their inv.
                    DeleteInventoryItem(sender, itemType, amount, x => x == sendersItem[0]);
                    break;
            }
        }

        [Command("drop")]
        public void drop_cmd(Client player, string item, int amount)
        {
            Character character = player.GetCharacter();

            //Get the item.
            var itemType = ParseInventoryItem(item);
            if (itemType == null)
            {
                API.sendNotificationToPlayer(player, "That item doesn't exist.");
                return;
            }
            var itemObj = ItemTypeToNewObject(itemType);
            if (itemObj.CanBeDropped == false)
            {
                API.sendNotificationToPlayer(player, "That item cannot be dropped.");
                return;
            }

            //Get in inv.
            var sendersItem = DoesInventoryHaveItem(character, itemType);
            if (sendersItem.Length != 1 || sendersItem[0].Amount < amount)
            {
                API.sendNotificationToPlayer(player, "You don't have that item or you don't have that amount or there is more than 1 item with that name.");
                return;
            }

            if(DeleteInventoryItem(character, itemType, amount, x => x == sendersItem[0]))
                API.sendNotificationToPlayer(player, "Item(s) was sucessfully dropped.");
        }

        #region Stashing System: 
        private Dictionary<NetHandle, IInventoryItem> stashedItems = new Dictionary<NetHandle, IInventoryItem>();

        [Command("stash")]
        public void stash_cmd(Client player, string item, int amount)
        {
            Character character = player.GetCharacter();

            //Get the item.
            var itemType = ParseInventoryItem(item);
            if (itemType == null)
            {
                API.sendNotificationToPlayer(player, "That item doesn't exist.");
                return;
            }
            var itemObj = ItemTypeToNewObject(itemType);
            if (itemObj.CanBeStashed == false)
            {
                API.sendNotificationToPlayer(player, "That item cannot be stashed.");
                return;
            }

            //Get in inv.
            var sendersItem = DoesInventoryHaveItem(character, itemType);
            if (sendersItem.Length != 1 || sendersItem[0].Amount < amount)
            {
                API.sendNotificationToPlayer(player, "You don't have that item or you don't have that amount.");
                return;
            }

            //Create object and add to list.
            var droppedObject = API.createObject(sendersItem[0].Object, player.position.Subtract(new Vector3(0, 0, 1)), new Vector3(0, 0, 0));
            stashedItems.Add(droppedObject, CloneItem(sendersItem[0], amount));

            //Decrease.
            DeleteInventoryItem(character, itemType, amount, x => x == sendersItem[0]);

            //Send message.
            API.sendNotificationToPlayer(player, $"You have sucessfully stashed ~g~{amount} {sendersItem[0].LongName}~w~. Use /pickupstash to take it.");
        }

        [Command("pickupstash")]
        public void pickupstash_cmd(Client player)
        {
            //Check if near any stash.
            var items = stashedItems.Where(x => API.getEntityPosition(x.Key).DistanceTo(player.position) <= 3).ToArray();
            if (!items.Any())
            {
                API.sendNotificationToPlayer(player, "You aren't near any stash.");
                return;
            }

            //Just get the first one and take it.
            Character character = player.GetCharacter();
            switch (GiveInventoryItem(character, items.First().Value))
            {
                case GiveItemErrors.NotEnoughSpace:
                    API.sendNotificationToPlayer(player, "You don't have enough space in his inventory.");
                    break;

                case GiveItemErrors.HasBlockingItem:
                    API.sendNotificationToPlayer(player, "You have a blocking item in hand.");
                    break;

                case GiveItemErrors.Success:
                    API.sendNotificationToPlayer(player,
                        $"You have sucessfully taken ~g~{items.First().Value.Amount}~w~ ~g~{items.First().Value.LongName}~w~ from the stash.");

                    //Remove object and item from list.
                    API.deleteEntity(items.First().Key);
                    stashedItems.Remove(items.First().Key);
                    break;
            }
        }

        #endregion

        [Command("inventory", Alias = "inv")]
        public void showinventory_cmd(Client player)
        {
            //TODO: For now can be just text-based even though I'd recommend it to be a CEF.
            Character character = player.GetCharacter();

            //First the main thing.
            API.sendChatMessageToPlayer(player, "-------------------------------------------------------------");
            API.sendChatMessageToPlayer(player, $"[INVENTORY] {GetInventoryFilledSlots(character)}/{character.MaxInvStorage} Slots [INVENTORY]");
            
            //For Each item.
            foreach (var item in character.Inventory)
            {
                API.sendChatMessageToPlayer(player, $"* ~r~{item.LongName}~w~[{item.CommandFriendlyName}] ({item.Amount}) Weights {item.AmountOfSlots} Slots" + (item.IsBlocking ? " [BLOCKING]" : ""));
            }

            //Ending
            API.sendChatMessageToPlayer(player, "-------------------------------------------------------------");
        }

        //TODO: TEST COMMAND.
        [Command("givemeitem")]
        public void GiveMeItem(Client player, string item, int amount)
        {
            Character character = player.GetCharacter();
            Type itemType = ParseInventoryItem(item);
            if (itemType != null)
            {
                var actualitem = ItemTypeToNewObject(itemType);
                switch (GiveInventoryItem(character, actualitem, amount))
                {
                    case GiveItemErrors.NotEnoughSpace:
                        API.sendChatMessageToPlayer(player, "You can't hold anymore items in your inventory.");
                        break;
                    case GiveItemErrors.Success:
                        API.sendChatMessageToPlayer(player, "DONE!");
                        break;
                }
            }
            else
                API.sendChatMessageToPlayer(player, "Invalid item name.");
        }
    }
}
