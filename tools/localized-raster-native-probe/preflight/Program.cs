System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { geometry = TilePreflight.Run(), formalScorer = FormalPreflight.Run("tools/localized-raster-native-probe/holdout") }));
