using PKHeX.Core;

namespace SysBot.Pokemon.Twitch
{
    public class TwitchQueue<T> where T : PKM, new()
    {
        public TwitchQueue(T pkm, PokeTradeTrainerInfo trainer, string username, bool subscriber)
        {
            Pokemon = pkm;
            Trainer = trainer;
            UserName = username;
            IsSubscriber = subscriber;
        }

        public string DisplayName => Trainer.TrainerName;

        public bool IsSubscriber { get; }

        public T Pokemon { get; }

        public PokeTradeTrainerInfo Trainer { get; }

        public string UserName { get; }
    }
}
