using System.Collections.Generic;
using UnityEngine;

namespace KiBird.MainMenu
{
    /// <summary>Persistance des scores via PlayerPrefs : dernier score, meilleur score, top 5.</summary>
    public static class ScoreManager
    {
        private const string LastScoreKey = "KiBird_LastScore";
        private const string LeaderboardKey = "KiBird_Leaderboard";
        private const int MaxEntries = 5;

        /// <summary>Meilleur score de la session active, conservé entre les rechargements de scène.</summary>
        public static int SessionBestScore { get; private set; }

        public static int GetLastScore() => PlayerPrefs.GetInt(LastScoreKey, 0);

        public static int GetBestScore()
        {
            List<int> top = GetTopScores();
            int topScore = top.Count > 0 ? top[0] : 0;
            return Mathf.Max(SessionBestScore, topScore);
        }

        public static void UpdateSessionBest(int score)
        {
            if (score > SessionBestScore)
            {
                SessionBestScore = score;
            }
        }

        /// <summary>Meilleurs scores enregistrés sur ce poste, du plus élevé au plus faible.</summary>
        public static List<int> GetTopScores()
        {
            var scores = new List<int>();
            string raw = PlayerPrefs.GetString(LeaderboardKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return scores;

            foreach (string entry in raw.Split(','))
            {
                if (int.TryParse(entry, out int value))
                {
                    scores.Add(value);
                }
            }
            scores.Sort((a, b) => b.CompareTo(a));
            return scores;
        }

        public static void AddScore(int score)
        {
            UpdateSessionBest(score);
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
