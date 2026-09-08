using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KiBird.MainMenu
{
    // Persistance simple via PlayerPrefs (pas de backend pour la JPO).
    // Le jeu appellera AddScore() en fin de partie.
    public static class ScoreManager
    {
        private const string LastScoreKey = "KiBird_LastScore";
        private const string LeaderboardKey = "KiBird_Leaderboard";
        private const int MaxEntries = 5;

        // Valeurs de démo affichées tant qu'aucune partie n'a été jouée sur ce poste.
        private static readonly int[] SeedScores = { 150, 120, 95, 80, 60 };

        public static int GetLastScore() => PlayerPrefs.GetInt(LastScoreKey, 0);

        public static List<int> GetTopScores()
        {
            string raw = PlayerPrefs.GetString(LeaderboardKey, string.Empty);
            if (string.IsNullOrEmpty(raw))
            {
                return SeedScores.ToList();
            }

            return raw.Split(',')
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToList();
        }

        public static void AddScore(int score)
        {
            PlayerPrefs.SetInt(LastScoreKey, score);

            List<int> scores = GetTopScores();
            scores.Add(score);
            scores.Sort((a, b) => b.CompareTo(a));
            if (scores.Count > MaxEntries)
            {
                scores.RemoveRange(MaxEntries, scores.Count - MaxEntries);
            }

            PlayerPrefs.SetString(LeaderboardKey, string.Join(",", scores));
            PlayerPrefs.Save();
        }
    }
}
