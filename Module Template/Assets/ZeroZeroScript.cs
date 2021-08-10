using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.RegularExpressions;
using UnityEngine;
using KModkit;
using UnityEngine.UI;

public class ZeroZeroScript: MonoBehaviour {

    public KMBombInfo Bomb;
    public KMAudio Audio;
    public KMBombModule Module;

    public KMSelectable[] starButtons, gridButtons;
    public MeshRenderer[] starIcons, gridColors;
    public Texture[] starTextures;
    public Transform[] starRots;

    private bool snVowel, snSumEven;
    private int numModules;

    private int originPos, redPos, greenPos, bluePos;  // reading order positions of the origin and colored squares
    private int[] keyCorners = new int[3]; // key corners for red, green, and blue
    private int leftover; // the non-key corner
    private readonly int[] positiveCorners = new int[3]; // the correct presses for red, green, and blue
    private readonly int[] coordinates = new int[6]; // the x and y coordinates for red, green, and blue
    private readonly Star[] stars = new Star[4]; // the corner stars, in reading order
    private int gameState = 0; // 0: to submit origin   1-3: to submit a positive quadrant    4: solved module

    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;
    String headers = "ABCDEFG1234567";

    void Awake () {
        moduleId = moduleIdCounter++;
    }

    void Start () {
        snVowel = Bomb.GetSerialNumber().Any(ch => "AEIOU".Contains(ch));
        snSumEven = Bomb.GetSerialNumberNumbers().Sum() % 2 == 0;
        numModules = Bomb.GetModuleNames().Count();
        Dictionary<Color, String> colorNames = initializeColorNames(); // dictionary for cleaner log
        Dictionary<int, String> cornerNames = initializeCornerNames(); // dictionary for cleaner log

        for (int i = 0; i < 49; i++) gridButtons[i].OnInteract = gridPress(i);
        for (int i = 0; i < 4; i++) starButtons[i].OnInteract = starPress(i);

        bool colinear; // are the spaces along the same line? e.g. A2 C3 E4
        do {
            List<int> freeSpaces = new List<int>(); // available spaces
            for (int i = 0; i < 49; i++) {
                freeSpaces.Add(i);
            }

            redPos = findPos(freeSpaces);
            greenPos = findPos(freeSpaces);
            bluePos = findPos(freeSpaces);
            List<int> tempPosList = new List<int>() {redPos, greenPos, bluePos};
            tempPosList.Sort();
            colinear = (tempPosList[2] - tempPosList[1]) % (tempPosList[1] - tempPosList[0]) == 0
                       || (tempPosList[1] - tempPosList[0]) % (tempPosList[2] - tempPosList[1]) == 0;
        } while (colinear);

        List<int> posList = new List<int>() {redPos, greenPos, bluePos};
        List<Color> rgb = new List<Color>() {Color.red, Color.green, Color.blue};

        for (int i = 0; i < 3; i++) {
            gridColors[posList[i]].material.color = rgb[i];
            Debug.LogFormat("[Zero, Zero #{0}] The {1} square is at {2}.", moduleId, colorNames[rgb[i]], convertToCoordinate(posList[i]));
        }

        do {
            originPos = UnityEngine.Random.Range(0, 49);
            foreach (int pos in posList) {
                if (originPos % 7 == pos % 7 || originPos / 7 == pos / 7) {
                    originPos = -1;
                    break;
                }
            }
        } while (originPos == -1);
        
        // gridColors[originPos].material.color = Color.yellow; // testing

        // key corners diamond, will fix if i figure out a neat method
        switch (numModules % 8 + (snSumEven ? 8 : 0)) { // cw -> +8
            case 0:
            case 14:
                keyCorners = new[] {2, 0, 1};
                break;
            case 1:
                keyCorners = new[] {0, 3, 2};
                break;
            case 2:
            case 8:
                keyCorners = new[] {3, 1, 0};
                break;
            case 3:
                keyCorners = new[] {0, 2, 1};
                break;
            case 4:
            case 10:    
                keyCorners = new[] {1, 3, 2};
                break;
            case 5:
                keyCorners = new[] {3, 0, 1};
                break;
            case 6:
            case 12:    
                keyCorners = new[] {1, 3, 0};
                break;
            case 7:
                keyCorners = new[] {3, 1, 2};
                break;
            case 9:
                keyCorners = new[] {2, 0, 3};
                break;
            case 11:
                keyCorners = new[] {2, 1, 0};
                break;
            case 13:
                keyCorners = new[] {1, 3, 0};
                break;
            case 15:
                keyCorners = new[] {1, 2, 3};
                break;
        }

        // picks positive quadrant for red, green, and blue
        for (int i = 0; i < 3; i++) {
            // diagonal from TL to BR
            if (Math.Abs(originPos - posList[i]) % 8 == 0) {
                positiveCorners[i] = (UnityEngine.Random.Range(0, 2) == 0 ? 0 : 3);
            }
            
            // diagonal from TR to BL
            else if (Math.Abs(originPos - posList[i]) % 6 == 0)
            {
                positiveCorners[i] = UnityEngine.Random.Range(1, 3);
            }
            
            // free choice
            else
            {
                positiveCorners[i] = UnityEngine.Random.Range(0, 4);
            }
        }
        
        // calculate actual coordinate pairs (x, then y) for red, green, and blue
        for (int i = 0; i < 3; i++) {
            coordinates[2 * i] = (posList[i] % 7 - originPos % 7) * (positiveCorners[i] % 2 == 0 ? -1 : 1);
            coordinates[2 * i + 1] = (posList[i] / 7 - originPos / 7) * (positiveCorners[i] < 2 ? -1 : 1);
            Debug.LogFormat("[Zero, Zero #{0}] The {1} coordinate pair is ({2}, {3}).", moduleId, colorNames[rgb[i]], coordinates[2 * i], coordinates[2 * i + 1]);
        }

        // initializes stars
        for (int i = 0; i < 4; i++) stars[i] = new Star();

        // pass through red, then green, then blue
        for (int i = 0; i < 3; i++) {
            List<int> nonKeyCorners = new List<int>() {0, 1, 2, 3};
            nonKeyCorners.Remove(keyCorners[i]);
            Star currentKey = stars[keyCorners[i]];

            // sets channel status in each non-key corner using relevant binary digit
            for (int j = 0; j < 3; j++) {
                stars[nonKeyCorners[j]].setChannel(Math.Abs(coordinates[2 * i]) % Math.Pow(2, 3 - j) >= Math.Pow(2, 2 - j), i);
            }
            stars[keyCorners[i]].setChannel(coordinates[2 * i] >= 0, i); // negates first coordinate if absent

            currentKey.setPoints(8 - Math.Abs(coordinates[2 * i + 1])); // sets point total in key corner
            starIcons[keyCorners[i]].material.SetTexture("_MainTex", starTextures[currentKey.getPoints() - 2]);

            currentKey.setDir(snVowel ^ coordinates[2 * i + 1] < 0); // spins in corresponding direction
        }

        List<int> leftoverList = new List<int> {0, 1, 2, 3};
        leftoverList.RemoveAll(i => keyCorners.Contains(i));
        leftover = leftoverList[0];
        
        stars[leftover].setPoints(UnityEngine.Random.Range(2, 9)); // sets point total for unused corner
        starIcons[leftover].material.SetTexture("_MainTex", starTextures[stars[leftover].getPoints() - 2]);
        stars[leftover].setDir(UnityEngine.Random.Range(0, 2) == 0);

        for (int i = 0; i < 4; i++) {
            Debug.LogFormat("[Zero, Zero #{0}] The {1} star is colored {2}, has {3} points, and is moving {4}clockwise.",
                moduleId, cornerNames[i], colorNames[stars[i].getStarColor()], stars[i].getPoints(), (stars[i].isClockwise() ? "" : "counter-"));
        }
        Debug.LogFormat("[Zero, Zero #{0}] The key corners for red, green, and blue are {1}, {2}, and {3}.", moduleId,
            cornerNames[keyCorners[0]], cornerNames[keyCorners[1]], cornerNames[keyCorners[2]]);
        Debug.LogFormat("[Zero, Zero #{0}] The origin is at {1}.", moduleId, convertToCoordinate(originPos));
        Debug.LogFormat("[Zero, Zero #{0}] The correct quadrants in order are {1}, {2}, and {3}.",
            moduleId, cornerNames[positiveCorners[0]], cornerNames[positiveCorners[1]], cornerNames[positiveCorners[2]]);
    }

    void Update ()
    {
        for (int i = 0; i < 4; i++) {
            starRots[i].RotateAround(starRots[i].position, starRots[i].forward,
                (stars[i].getDirection() ? -1 : 1) * 40 * Time.deltaTime);
        }
    }
    
    // finds a valid space for the color, and updates the list's valid squares
    private int findPos(List<int> list) {
        int result = list[UnityEngine.Random.Range(0, list.Count)];
        int resultRow = result / 7;
        int resultCol = result % 7;
        
        for (int i = list.Count - 1; i >= 0; i--) {
            int pos = list[i];
            int row = Math.Abs((pos / 7) - resultRow);
            int col = Math.Abs((pos % 7) - resultCol);
            if (row == 0 || col == 0 || row == col) {
                list.Remove(pos);
            }
        }
        return result;
    }

    // converts the integer position in the 7x7 grid to an Excel coordinate pair
    private String convertToCoordinate(int pos) {
        return "" + headers[pos % 7] + headers[pos / 7 + 7];
    }

    // initializes the dictionary which converts the inbuilt Color class to its name
    private Dictionary<Color, String> initializeColorNames() {
        return new Dictionary<Color, String> {
            {Color.white, "white"},
            {Color.yellow, "yellow"},
            {Color.magenta, "magenta"},
            {Color.red, "red"},
            {Color.cyan, "cyan"},
            {Color.green, "green"},
            {Color.blue, "blue"},
            {Color.black, "black"}
        };
    }

    // initializes the dictionary which converts the corner positions to their relative positions
    private Dictionary<int, String> initializeCornerNames() {
        return new Dictionary<int, string> {
            {0, "top left"},
            {1, "top right"},
            {2, "bottom left"},
            {3, "bottom right"}
        };
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Use <!{0} B3> to press the button in the second column and third row. Use <!{0} top right> or <!{0} tr> to press the top-right screen.";
    #pragma warning restore 414

    // handles interactions inside the 7x7 grid
    private KMSelectable.OnInteractHandler gridPress(int i) {
        return delegate {
            if (gameState == 4) {
                return false;
            }
            
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, gridButtons[i].transform);
            gridButtons[i].AddInteractionPunch(.1f);
            if (gameState != 0) {
                Module.HandleStrike();
                Debug.LogFormat("[Zero, Zero #{0}] A corner screen was not pressed. Strike.", moduleId);
            }
            else if (i != originPos) {
                Module.HandleStrike();
                Debug.LogFormat("[Zero, Zero #{0}] {1} was pressed. Strike.", moduleId, convertToCoordinate(i));
            }
            else {
                gameState = 1;
                Debug.LogFormat("[Zero, Zero #{0}] {1} was pressed. That was correct.", moduleId, convertToCoordinate(i));
            }

            return false;
        };
    }
    
    // handles interactions with the four corner screens
    private KMSelectable.OnInteractHandler starPress(int i) {
        Dictionary<int, String> cornerNames = initializeCornerNames(); // dictionary for cleaner log
        
        return delegate {
            if (gameState == 4) {
                return false;
            }
            
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, starButtons[i].transform);
            starButtons[i].AddInteractionPunch(.1f);
            if (gameState == 0) {
                Module.HandleStrike();
                Debug.LogFormat("[Zero, Zero #{0}] A grid button was not pressed. Strike.", moduleId);
            }
            else if (i != positiveCorners[gameState - 1]) {
                Module.HandleStrike();
                Debug.LogFormat("[Zero, Zero #{0}] The {1} corner was pressed. Strike.", moduleId, cornerNames[i]);
                gameState = 0;
            }
            else if (gameState == 3) {
                Module.HandlePass();
                moduleSolved = true;
                Audio.PlaySoundAtTransform("corner" + gameState, Module.transform);
                Debug.LogFormat("[Zero, Zero #{0}] The {1} corner was pressed. Module solved.", moduleId, cornerNames[i]);
                gameState = 4;
            }
            else {
                Audio.PlaySoundAtTransform("corner" + gameState, Module.transform);
                Debug.LogFormat("[Zero, Zero #{0}] The {1} corner was pressed. That was correct.", moduleId, cornerNames[i]);
                gameState++;
            }

            return false;
        };
    }

    IEnumerator ProcessTwitchCommand (string command) {
        command = command.Trim().ToUpperInvariant();
        Match m;
        List<String> cornerNames = new List<String> {"TOP LEFT", "TOP RIGHT", "BOTTOM LEFT", "BOTTOM RIGHT",
            "TOP-LEFT", "TOP-RIGHT", "BOTTOM-LEFT", "BOTTOM-RIGHT", "TL", "TR", "BL", "BR"};

        if ((m = Regex.Match(command, "[A-G][1-7]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success) {
            gridButtons[headers.IndexOf(command[0]) + (headers.IndexOf(command[1]) - 7) * 7].OnInteract();
        }
        else if (cornerNames.Contains(command)) {
            starButtons[cornerNames.IndexOf(command) % 4].OnInteract();
        }
        yield return null;
    }

    IEnumerator TwitchHandleForcedSolve () {
        yield return null;
    }
}

public class Star {
    private readonly bool[] channels = new bool[3];
    private int points;
    private bool cw;

    public Color getStarColor() {
        if (channels[0] && channels[1] && channels[2]) {
            return Color.white;
        }
        else if (channels[0] && channels[1] && !channels[2]) {
            return Color.yellow;
        }
        else if (channels[0] && !channels[1] && channels[2]) {
            return Color.magenta;
        }
        else if (channels[0] && !channels[1] && !channels[2]) {
            return Color.red;
        }
        else if (!channels[0] && channels[1] && channels[2]) {
            return Color.cyan;
        }
        else if (!channels[0] && channels[1] && !channels[2]) {
            return Color.green;
        }
        else if (!channels[0] && !channels[1] && channels[2]) {
            return Color.blue;
        }
        else {
            return Color.black;
        }
    }

    public int getPoints() {
        return points;
    }

    public bool getDirection() {
        return cw;
    }
    
    public bool isClockwise() {
        return cw;
    }
    
    public void setChannel(bool present, int index) {
        channels[index] = present;
    }

    public void setPoints(int p) {
        points = p;
    }

    public void setDir(bool direction) {
        cw = direction;
    }
}
