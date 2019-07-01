using System;
using MongoDB.Bson;

namespace mtgvrp.AdminSystem
{
    public class PlayerWarns
    {
        public PlayerWarns(string warnReceiverId, string warnSenderId, string reason)
        {
            WarnReceiver = warnReceiverId;
            WarnSender = warnSenderId;
            WarnReason = reason;
            DateTime = DateTime.Now;
        }

        public ObjectId Id { get; set; }

        public string WarnReceiver { get; set; }
        public string WarnSender { get; set; }
        public string WarnReason { get; set; }
        public DateTime DateTime { get; set; }

        public void Insert()
        {
            //DatabaseManager.PlayerWarnTable.InsertOne(this);
        }
    }
}