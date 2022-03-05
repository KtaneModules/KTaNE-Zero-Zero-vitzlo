using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using KModkit;
using rnd = UnityEngine.Random;

public class ZeroZeroScript: MonoBehaviour {
    public KMBombInfo Bomb;
    public KMAudio Audio;
    public KMBombModule Module;
    private string versionNumber = "1.2.2";

    public KMSelectable[] starButtons, gridButtons;
    public MeshRenderer[] gridColors;
    public SpriteRenderer[] starIcons;
    public Sprite[] starTextures, outlineTextures;
    public Transform[] starRots;

    private readonly Coroutine[] starCoroutines = new Coroutine[4];
    private readonly float[] starCoroutineDeltas = new float[4];
    
    private readonly Dictionary<Color, string> colorNames = new Dictionary<Color, string> {
        {Color.white, "white"}, {Color.yellow, "yellow"}, {Color.magenta, "magenta"}, {Color.red, "red"},
        {Color.cyan, "cyan"}, {Color.green, "green"}, {Color.blue, "blue"}, {Color.black, "black"}
    };
    private readonly List<Color> rgb = new List<Color>() {Color.red, Color.green, Color.blue};
    private readonly Dictionary<int, string> cornerNames = new Dictionary<int, string> {
        {0, "top-left"}, {1, "top-right"}, {2, "bottom-left"}, {3, "bottom-right"}
    };

    private int originPos, redPos, greenPos, bluePos;  // reading order positions of the origin and colored squares
    List<int> freeSpaces = new List<int>(), posList = new List<int>();
    private int[] keyCorners = new int[3]; // key corners for red, green, and blue
    private readonly int[] positiveCorners = new int[3]; // the correct presses for red, green, and blue
    private readonly int[] coordinates = new int[6]; // the x and y coordinates for red, green, and blue
    private readonly Star[] stars = {new Star(), new Star(), new Star(), new Star()}; // the corner stars, in reading order
    private int gameState; // 0: to submit origin   1-3: to submit a positive quadrant    4: solved module

    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;

    void Awake () {
        moduleId = moduleIdCounter++;
    }

    void Start () {
        Debug.LogFormat("[Zero, Zero #{0}] Version: {1}", moduleId, versionNumber);

        for (int i = 0; i < 49; i++) {
            gridButtons[i].OnInteract = GridPress(i);
        }
        for (int i = 0; i < 4; i++) {
            starButtons[i].OnInteract = StarPress(i);
        }
        
        FindPositions();
        posList = new List<int> {redPos, greenPos, bluePos};
        for (int i = 0; i < 3; i++) {
            gridColors[posList[i]].material.color = rgb[i];
            Debug.LogFormat("[Zero, Zero #{0}] The {1} square is at {2}.", moduleId, colorNames[rgb[i]], ConvertToCoordinate(posList[i]));
        }
        originPos = freeSpaces[rnd.Range(0, freeSpaces.Count)];
        // gridColors[originPos].material.color = Color.yellow; // testing

        // key corners diamond, will clean up if i figure out a neat method // cw -> +8
        keyCorners = new[] {
            new[] {2, 0, 1}, new[] {0, 3, 2}, new[] {3, 1, 0}, new[] {0, 2, 1}, new[] {1, 3, 2}, new[] {3, 0, 1}, new[] {0, 2, 3}, new[] {3, 1, 2},
            new[] {3, 1, 0}, new[] {2, 0, 3}, new[] {1, 3, 2}, new[] {2, 1, 0}, new[] {0, 2, 3}, new[] {1, 3, 0}, new[] {2, 0, 1}, new[] {1, 2, 3}
        }[Bomb.GetModuleNames().Count % 8 + (Bomb.GetSerialNumberNumbers().Sum() % 2 == 0 ? 8 : 0)];
        
        AssignCoordinates();

        InitializeStars();
    }

    void Update () {
        for (int i = 0; i < 4; i++) {
            starRots[i].RotateAround(starRots[i].position, starRots[i].forward,
                (stars[i].Cw ? -1 : 1) * 40 * Time.deltaTime);
        }
    }

    // fades out the star at the given position to black, or back to full color if reverse is true
    private IEnumerator FadeOut(int pos, bool reverse = false) {
        Color colorCache = starIcons[pos].color;
        colorCache.a = 1;
        while (starCoroutineDeltas[pos] < 1) {
            starCoroutineDeltas[pos] += Time.deltaTime * 0.5f;
            starIcons[pos].color = (reverse
                ? Color.Lerp(colorCache, stars[pos].GetStarColor() == Color.black ? Color.white : stars[pos].GetStarColor(), starCoroutineDeltas[pos])
                : Color.Lerp(colorCache, Color.clear, starCoroutineDeltas[pos]));
            yield return null;
        }
        starCoroutineDeltas[pos] = 0;
    }

    // fades all stars up to the specified position back to their original colors (following a strike)
    private void FadeIn(int maxPos) {
        for (int i = 0; i < maxPos; i++) {
            StopCoroutine(starCoroutines[i]);
            StartCoroutine(FadeOut(i, true));
        }
    }
    
    // fades the grid colors to white once the module is solved
    private IEnumerator FadeColors() {
        Color[] colorCache = {Color.red, Color.green, Color.blue,};
        float delta = 0;
        while (delta < 1) {
            delta += Time.deltaTime * 0.5f;
            for (int i = 0; i < 3; i++) {
                gridColors[posList[i]].material.color = (Color.Lerp(colorCache[i], Color.white, delta));
            }
            yield return null;
        }
    }

    private void FindPositions() {
        freeSpaces = Enumerable.Range(0, 49).ToList(); // available spaces

        redPos = FindPos();
        greenPos = FindPos();
        bluePos = FindPos();
        List<int> tempPosList = new List<int>() {redPos, greenPos, bluePos};
        tempPosList.Sort();
        if ((tempPosList[2] - tempPosList[1]) % (tempPosList[1] - tempPosList[0]) == 0
            || (tempPosList[1] - tempPosList[0]) % (tempPosList[2] - tempPosList[1]) == 0) {
            FindPositions();
        }
    }
    
    // finds a valid space for the color, and updates the list's valid squares (no mutual queen-capturing)
    private int FindPos() {
        int result = freeSpaces[rnd.Range(0, freeSpaces.Count)];
        int resultRow = result / 7;
        int resultCol = result % 7;

        freeSpaces = freeSpaces.Where(p => p / 7 != resultRow)
            .Where(p => p % 7 != resultCol)
            .Where(p => Math.Abs(p % 7 - resultCol) != Math.Abs(p / 7 - resultRow)).ToList();
        
        return result;
    }

    private void AssignCoordinates() {
        // picks positive quadrants for red, green, and blue
        for (int i = 0; i < 3; i++) {
            if (originPos % 7 - posList[i] % 7 == originPos / 7 - posList[i] / 7) {
                positiveCorners[i] = rnd.Range(0, 2) == 0 ? 0 : 3; // diagonal from TL to BR
            } else if (originPos % 7 - posList[i] % 7 == posList[i] / 7 - originPos / 7) {
                positiveCorners[i] = rnd.Range(1, 3); // diagonal from TR to BL
            } else {
                positiveCorners[i] = rnd.Range(0, 4); // free choice
            }
        }
        
        // calculate actual coordinate pairs (x, then y) for red, green, and blue
        for (int i = 0; i < 3; i++) {
            coordinates[2 * i] = (posList[i] % 7 - originPos % 7) * (positiveCorners[i] % 2 == 0 ? -1 : 1);
            coordinates[2 * i + 1] = (posList[i] / 7 - originPos / 7) * (positiveCorners[i] < 2 ? -1 : 1);
            Debug.LogFormat("[Zero, Zero #{0}] The {1} coordinate pair is ({2}, {3}).", moduleId, colorNames[rgb[i]], coordinates[2 * i], coordinates[2 * i + 1]);
        }
    }

    // creates, modifies, and logs the colors, directions, and point counts of the stars
    private void InitializeStars() {
        // pass through red, then green, then blue
        for (int i = 0; i < 3; i++) {
            List<int> nonKeyCorners = new List<int> {0, 1, 2, 3};
            nonKeyCorners.Remove(keyCorners[i]);
            Star currentKey = stars[keyCorners[i]];

            // sets channel status in each non-key corner using relevant binary digit
            for (int j = 0; j < 3; j++) {
                stars[nonKeyCorners[j]].SetChannel(Math.Abs(coordinates[2 * i]) % Math.Pow(2, 3 - j) >= Math.Pow(2, 2 - j), i);
            }
            stars[keyCorners[i]].SetChannel(coordinates[2 * i] >= 0, i); // negates first coordinate if absent

            currentKey.Points = (8 - Math.Abs(coordinates[2 * i + 1])); // sets point total in key corner

            currentKey.Cw = Bomb.GetSerialNumber().Any(c => "AEIOU".Contains(c)) ^ coordinates[2 * i + 1] < 0; // spins in corresponding direction
        }

        List<int> leftoverList = new List<int> {0, 1, 2, 3};
        leftoverList.RemoveAll(i => keyCorners.Contains(i));

        stars[leftoverList[0]].Points = rnd.Range(2, 9); // sets point total for unused corner
        stars[leftoverList[0]].Cw = rnd.Range(0, 2) == 0; // sets direction for unused corner

        for (int i = 0; i < 4; i++) {
            if (stars[i].GetStarColor() == Color.black) {
                starIcons[i].sprite = outlineTextures[stars[i].Points - 2];
            } else {
                starIcons[i].sprite = starTextures[stars[i].Points - 2];
                starIcons[i].color = stars[i].GetStarColor();
            }
        }
        
        for (int i = 0; i < 4; i++) {
            Debug.LogFormat("[Zero, Zero #{0}] The {1} star is colored {2}, has {3} points, and is moving {4}clockwise.",
                moduleId, cornerNames[i], colorNames[stars[i].GetStarColor()], stars[i].Points, (stars[i].Cw ? "" : "counter-"));
        }
        Debug.LogFormat("[Zero, Zero #{0}] The key corners for red, green, and blue are {1}, {2}, and {3}.", moduleId,
            cornerNames[keyCorners[0]], cornerNames[keyCorners[1]], cornerNames[keyCorners[2]]);
        Debug.LogFormat("[Zero, Zero #{0}] The origin is at {1}.", moduleId, ConvertToCoordinate(originPos));
        Debug.LogFormat("[Zero, Zero #{0}] The correct quadrants in order are {1}, {2}, and {3}.",
            moduleId, cornerNames[positiveCorners[0]], cornerNames[positiveCorners[1]], cornerNames[positiveCorners[2]]);
    }

    // converts the integer position in the 7×7 grid to an Excel coordinate pair
    private string ConvertToCoordinate(int pos) {
        return "" + (char)(pos % 7 + 'A') + (char)(pos / 7 + '1');
    }

    // handles interactions inside the 7x7 grid
    private KMSelectable.OnInteractHandler GridPress(int i) {
        return delegate {
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, gridButtons[i].transform);
            gridButtons[i].AddInteractionPunch(.1f);
            
            if (moduleSolved) {
                return false;
            }
            if (gameState != 0) {
                Module.HandleStrike();
                Debug.LogFormat("[Zero, Zero #{0}] A corner screen was not pressed. Strike.", moduleId);
                FadeIn(gameState);
                gameState = 0;
            }
            else if (i != originPos) {
                Module.HandleStrike();
                Debug.LogFormat("[Zero, Zero #{0}] {1} was pressed. Strike.", moduleId, ConvertToCoordinate(i));
            }
            else {
                gameState = 1;
                starCoroutines[0] = StartCoroutine(FadeOut(0));
                Debug.LogFormat("[Zero, Zero #{0}] {1} was pressed. That was correct.", moduleId, ConvertToCoordinate(i));
            }

            return false;
        };
    }
    
    // handles interactions with the four corner screens
    private KMSelectable.OnInteractHandler StarPress(int i) {
        return delegate {
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, starButtons[i].transform);
            starButtons[i].AddInteractionPunch(.1f);
            
            if (moduleSolved) {
                return false;
            }
            if (gameState == 0) {
                Module.HandleStrike();
                Debug.LogFormat("[Zero, Zero #{0}] A grid button was not pressed. Strike.", moduleId);
                FadeIn(gameState);
            }
            else if (i != positiveCorners[gameState - 1]) {
                Module.HandleStrike();
                Debug.LogFormat("[Zero, Zero #{0}] The {1} corner was pressed. Strike.", moduleId, cornerNames[i]);
                FadeIn(gameState);
                gameState = 0;
            }
            else if (gameState == 3) {
                Module.HandlePass();
                moduleSolved = true;
                Audio.PlaySoundAtTransform("corner3", Module.transform);
                Debug.LogFormat("[Zero, Zero #{0}] The {1} corner was pressed. Module solved.", moduleId, cornerNames[i]);
                starCoroutines[3] = StartCoroutine(FadeOut(3));
                StartCoroutine(FadeColors());
                gameState = 4;
            }
            else {
                Audio.PlaySoundAtTransform("corner" + gameState, Module.transform);
                Debug.LogFormat("[Zero, Zero #{0}] The {1} corner was pressed. That was correct.", moduleId, cornerNames[i]);
                starCoroutines[gameState] = StartCoroutine(FadeOut(gameState));
                gameState++;
            }

            return false;
        };
    }
    
    #pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Use <!{0} B3> to press the button in the second column and third row. Use <!{0} top right> or <!{0} tr> to press the top-right screen.";
    #pragma warning restore 414

    IEnumerator ProcessTwitchCommand(string command) {
        command = command.Trim().ToUpperInvariant();
        List<String> cornerOptions = new List<String> {"TOP LEFT", "TOP RIGHT", "BOTTOM LEFT", "BOTTOM RIGHT",
            "TOP-LEFT", "TOP-RIGHT", "BOTTOM-LEFT", "BOTTOM-RIGHT", "TL", "TR", "BL", "BR"};

        if (Regex.Match(command, "[A-G][1-7]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Success) {
            yield return null;
            gridButtons[command[0] - 'A' + (command[1] - '1') * 7].OnInteract();
        }
        else if (cornerOptions.Contains(command)) {
            yield return null;
            starButtons[cornerOptions.IndexOf(command) % 4].OnInteract();
        }
    }
}

public class Star {
    private readonly bool[] channels = new bool[3];
    public int Points;
    public bool Cw;

    // converts this star's channels into a proper color
    public Color GetStarColor() {
        Color[] colorOrdering = {Color.black, Color.blue, Color.green, Color.cyan, Color.red, Color.magenta, Color.yellow, Color.white};
        return colorOrdering[(channels[0] ? 4 : 0) + (channels[1] ? 2 : 0) + (channels[2] ? 1 : 0)];
    }

    public void SetChannel(bool present, int index) {
        channels[index] = present;
    }
}
