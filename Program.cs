using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BriscolaGreedy
{
    // Italian-suit deck
    public enum Seme { Oro, Spade, Bastoni, Coppe }

    // Strategies compared in the tournament (see PlayGreedy / PlayHoarder / PlayCounter)
    public enum StrategyId { Greedy, Hoarder, Counter }

    public record Carta(Seme Seme, int Rango, int Punti);

    // Shared per-player memory. Only public information (exposed briscola + played cards) is recorded.
    public class GameState
    {
        public HashSet<Carta> PlayedCards { get; } = new();
    }

    internal class Program
    {
        // Briscola strength order (index 0 = strongest): Asso(1), Tre(3), Re(10), Cavallo(9), Fante(8), 7..2.
        private static readonly int[] OrdineForza = { 1, 3, 10, 9, 8, 7, 6, 5, 4, 2 };

        // Thresholds used by Hoarder and Counter policies (see strategy comments below).
        private const int TauHoarder = 10;
        private const int TauCounter = 10;

        static int Main(string[] args)
        {
            int totalGames = 1_000_000;
            int seed = 42;
            string outputPath = "briscola_simulazione.csv";
            StrategyId[] strategies = { StrategyId.Greedy, StrategyId.Hoarder, StrategyId.Counter };

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--total-games": totalGames = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--seed":        seed = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--output":      outputPath = args[++i]; break;
                    default:
                        Console.Error.WriteLine($"Unknown argument: {args[i]}");
                        Console.Error.WriteLine("Usage: BriscolaGreedy [--total-games N] [--seed S] [--output PATH]");
                        return 2;
                }
            }

            int numMatchups = strategies.Length * strategies.Length;
            int gamesPerMatch = totalGames / numMatchups;
            int totalActualGames = gamesPerMatch * numMatchups;

            Console.WriteLine("Briscola round-robin Monte Carlo tournament");
            Console.WriteLine($"  Strategies        : {string.Join(", ", strategies)}");
            Console.WriteLine($"  Ordered matchups  : {numMatchups}");
            Console.WriteLine($"  Games per matchup : {gamesPerMatch:N0}");
            Console.WriteLine($"  Total games       : {totalActualGames:N0}");
            Console.WriteLine($"  RNG seed          : {seed}");
            Console.WriteLine($"  Output file       : {outputPath}");
            Console.WriteLine();

            var rng = new Random(seed);
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
            using var writer = new StreamWriter(fs, new UTF8Encoding(false), 1 << 20);

            writer.WriteLine("PartitaId;MatchId;StrategyG1;StrategyG2;Mano;SemeBriscola;" +
                             "CartaG1;CartaG2;VincitoreMano;PuntiMano;BriscoleTotaliG1;BriscoleTotaliG2;" +
                             "VincitorePartita;PuntiFinaliG1;PuntiFinaliG2");

            int partitaId = 0;
            int matchId = 0;
            foreach (var stratG1 in strategies)
            {
                foreach (var stratG2 in strategies)
                {
                    matchId++;
                    Console.WriteLine($"[Match {matchId}/{numMatchups}] {stratG1} (G1) vs {stratG2} (G2) — {gamesPerMatch:N0} games");
                    for (int g = 1; g <= gamesPerMatch; g++)
                    {
                        partitaId++;
                        SimulaPartita(partitaId, matchId, stratG1, stratG2, rng, writer);
                        if (g % 50_000 == 0)
                            Console.WriteLine($"    {g:N0}/{gamesPerMatch:N0}");
                    }
                }
            }

            writer.Flush();
            Console.WriteLine();
            Console.WriteLine($"Simulation complete. Output: {outputPath}");
            return 0;
        }

        static void SimulaPartita(int id, int matchId, StrategyId stratG1, StrategyId stratG2, Random rng, StreamWriter writer)
        {
            var mazzo = InizializzaMazzo();
            Shuffle(mazzo, rng);

            var briscolaCarta = mazzo[^1];
            var semeBriscola = briscolaCarta.Seme;

            var manoG1 = mazzo.GetRange(0, 3);
            var manoG2 = mazzo.GetRange(3, 3);
            mazzo.RemoveRange(0, 6);

            int puntiG1 = 0, puntiG2 = 0;
            int briscoleTotG1 = manoG1.Count(c => c.Seme == semeBriscola);
            int briscoleTotG2 = manoG2.Count(c => c.Seme == semeBriscola);

            var stateG1 = new GameState();
            var stateG2 = new GameState();
            stateG1.PlayedCards.Add(briscolaCarta);
            stateG2.PlayedCards.Add(briscolaCarta);

            int leader = 1;
            var log = new List<string>(20);

            for (int m = 1; m <= 20; m++)
            {
                Carta leadCard, respCard;
                int leaderPlayer = leader;
                int responderPlayer = leader == 1 ? 2 : 1;

                if (leaderPlayer == 1)
                {
                    leadCard = Play(stratG1, manoG1, null, semeBriscola, stateG1);
                    respCard = Play(stratG2, manoG2, leadCard, semeBriscola, stateG2);
                }
                else
                {
                    leadCard = Play(stratG2, manoG2, null, semeBriscola, stateG2);
                    respCard = Play(stratG1, manoG1, leadCard, semeBriscola, stateG1);
                }

                int leaderWins = DeterminaVincitoreTrick(leadCard, respCard, semeBriscola);
                int vincitoreMano = leaderWins == 1 ? leaderPlayer : responderPlayer;

                Carta cartaG1 = leaderPlayer == 1 ? leadCard : respCard;
                Carta cartaG2 = leaderPlayer == 2 ? leadCard : respCard;

                stateG1.PlayedCards.Add(cartaG1); stateG1.PlayedCards.Add(cartaG2);
                stateG2.PlayedCards.Add(cartaG1); stateG2.PlayedCards.Add(cartaG2);

                int puntiInPalio = cartaG1.Punti + cartaG2.Punti;
                if (vincitoreMano == 1) puntiG1 += puntiInPalio; else puntiG2 += puntiInPalio;

                log.Add($"{id};{matchId};{stratG1};{stratG2};{m};{semeBriscola};{cartaG1};{cartaG2};{vincitoreMano};{puntiInPalio}");

                if (mazzo.Count > 0)
                {
                    var p1 = mazzo[0]; mazzo.RemoveAt(0);
                    var p2 = mazzo[0]; mazzo.RemoveAt(0);
                    if (vincitoreMano == 1)
                    {
                        manoG1.Add(p1); manoG2.Add(p2);
                        if (p1.Seme == semeBriscola) briscoleTotG1++;
                        if (p2.Seme == semeBriscola) briscoleTotG2++;
                    }
                    else
                    {
                        manoG2.Add(p1); manoG1.Add(p2);
                        if (p1.Seme == semeBriscola) briscoleTotG2++;
                        if (p2.Seme == semeBriscola) briscoleTotG1++;
                    }
                }

                leader = vincitoreMano;
            }

            string vincitoreFinale = puntiG1 > puntiG2 ? "G1" : puntiG1 < puntiG2 ? "G2" : "Pareggio";
            foreach (var line in log)
                writer.WriteLine($"{line};{briscoleTotG1};{briscoleTotG2};{vincitoreFinale};{puntiG1};{puntiG2}");
        }

        // ---------------------------------------------------------------------
        // Strategies
        // ---------------------------------------------------------------------

        static Carta Play(StrategyId s, List<Carta> hand, Carta? opp, Seme briscola, GameState state)
        {
            Carta chosen = s switch
            {
                StrategyId.Greedy  => PlayGreedy(hand, opp, briscola),
                StrategyId.Hoarder => PlayHoarder(hand, opp, briscola),
                StrategyId.Counter => PlayCounter(hand, opp, briscola, state),
                _ => throw new ArgumentOutOfRangeException(nameof(s))
            };
            hand.Remove(chosen);
            return chosen;
        }

        // Strategy pi_G : baseline greedy.
        //   Leader  : play the weakest non-briscola card (by points, then by strength).
        //   Follower: (i) cheapest same-suit winner, else (ii) cheapest briscola that overtrumps, else
        //             (iii) cheapest dump (any suit).
        static Carta PlayGreedy(List<Carta> mano, Carta? avv, Seme briscola)
        {
            if (avv is null)
                return mano.OrderBy(c => c.Seme == briscola).ThenBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).First();

            var sameSuitWin = mano.Where(c => c.Seme == avv.Seme && ForzaIdx(c.Rango) < ForzaIdx(avv.Rango))
                                  .OrderBy(c => c.Punti).ToList();
            if (sameSuitWin.Count > 0) return sameSuitWin[0];

            var myBriscole = mano.Where(c => c.Seme == briscola).OrderBy(c => c.Punti).ToList();
            var overtrump = myBriscole.FirstOrDefault(c => DeterminaVincitoreTrick(avv, c, briscola) == 2);
            if (overtrump is not null) return overtrump;

            return mano.OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).First();
        }

        // Strategy pi_H : briscola-hoarder (parsimonious).
        //   Leader  : never lead a briscola if any non-briscola is available.
        //   Follower: same-suit win if possible; overtrump ONLY when the opponent's card is worth
        //             at least TauHoarder points; otherwise dump the cheapest non-briscola.
        static Carta PlayHoarder(List<Carta> mano, Carta? avv, Seme briscola)
        {
            if (avv is null)
            {
                var nonBr = mano.Where(c => c.Seme != briscola).ToList();
                if (nonBr.Count > 0)
                    return nonBr.OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).First();
                return mano.OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).First();
            }

            var sameSuitWin = mano.Where(c => c.Seme == avv.Seme && ForzaIdx(c.Rango) < ForzaIdx(avv.Rango))
                                  .OrderBy(c => c.Punti).ToList();
            if (sameSuitWin.Count > 0) return sameSuitWin[0];

            if (avv.Punti >= TauHoarder)
            {
                var myBriscole = mano.Where(c => c.Seme == briscola).OrderBy(c => c.Punti).ToList();
                var overtrump = myBriscole.FirstOrDefault(c => DeterminaVincitoreTrick(avv, c, briscola) == 2);
                if (overtrump is not null) return overtrump;
            }

            var dumpNonBr = mano.Where(c => c.Seme != briscola).OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).ToList();
            if (dumpNonBr.Count > 0) return dumpNonBr[0];
            return mano.OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).First();
        }

        // Strategy pi_C : public-information counter.
        //   Maintains the set of cards already revealed (exposed briscola + cards played in prior tricks).
        //   Leader  : if holding a non-briscola carico (Asso=11 pts or Tre=10 pts) whose sibling carico
        //             of the same suit has already been seen, lead it (opponent cannot beat it in-suit
        //             and must choose between burning a briscola or letting the points go — a Nash-stable
        //             carico-trap). Otherwise lead the weakest non-briscola card.
        //   Follower: same-suit win; overtrump only when avv.Punti >= TauCounter; else dump cheapest non-briscola.
        static Carta PlayCounter(List<Carta> mano, Carta? avv, Seme briscola, GameState st)
        {
            if (avv is null)
            {
                foreach (var c in mano.Where(c => (c.Rango == 1 || c.Rango == 3) && c.Seme != briscola)
                                      .OrderByDescending(c => c.Punti))
                {
                    int siblingRango = c.Rango == 1 ? 3 : 1;
                    int siblingPunti = siblingRango == 1 ? 11 : 10;
                    var sibling = new Carta(c.Seme, siblingRango, siblingPunti);
                    if (st.PlayedCards.Contains(sibling)) return c;
                }

                var nonBr = mano.Where(c => c.Seme != briscola).ToList();
                if (nonBr.Count > 0)
                    return nonBr.OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).First();
                return mano.OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).First();
            }

            var sameSuitWin = mano.Where(c => c.Seme == avv.Seme && ForzaIdx(c.Rango) < ForzaIdx(avv.Rango))
                                  .OrderBy(c => c.Punti).ToList();
            if (sameSuitWin.Count > 0) return sameSuitWin[0];

            if (avv.Punti >= TauCounter)
            {
                var myBriscole = mano.Where(c => c.Seme == briscola).OrderBy(c => c.Punti).ToList();
                var overtrump = myBriscole.FirstOrDefault(c => DeterminaVincitoreTrick(avv, c, briscola) == 2);
                if (overtrump is not null) return overtrump;
            }

            var dumpNonBr = mano.Where(c => c.Seme != briscola).OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).ToList();
            if (dumpNonBr.Count > 0) return dumpNonBr[0];
            return mano.OrderBy(c => c.Punti).ThenBy(c => ForzaIdx(c.Rango)).First();
        }

        // ---------------------------------------------------------------------
        // Trick resolution
        // ---------------------------------------------------------------------

        // Returns 1 if the LEADER wins the trick, 2 if the RESPONDER wins.
        // This signature is the one bugfix vs. the original code: the tie-break on different non-briscola
        // suits must award the trick to whoever led, not to a fixed player id.
        static int DeterminaVincitoreTrick(Carta lead, Carta resp, Seme briscola)
        {
            if (lead.Seme == resp.Seme)
                return ForzaIdx(lead.Rango) < ForzaIdx(resp.Rango) ? 1 : 2;
            if (resp.Seme == briscola) return 2;
            if (lead.Seme == briscola) return 1;
            return 1;
        }

        static int ForzaIdx(int rango) => Array.IndexOf(OrdineForza, rango);

        static List<Carta> InizializzaMazzo()
        {
            var mazzo = new List<Carta>(40);
            foreach (Seme s in Enum.GetValues(typeof(Seme)))
            {
                for (int r = 1; r <= 10; r++)
                {
                    int p = r switch { 1 => 11, 3 => 10, 10 => 4, 9 => 3, 8 => 2, _ => 0 };
                    mazzo.Add(new Carta(s, r, p));
                }
            }
            return mazzo;
        }

        static void Shuffle<T>(List<T> list, Random rng)
        {
            int n = list.Count;
            while (n > 1)
            {
                int k = rng.Next(n--);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }
    }
}
