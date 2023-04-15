using System;
using System.Threading;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Microsoft.CognitiveServices.Speech;
using System.Threading.Tasks;
using SimpleJSON;

public class BotService : MonoBehaviour
{
    [SerializeField] public GameObject Avatar;
    [SerializeField] public TextMeshProUGUI OutputText;
    [SerializeField] public Button startRecoButton;
    [SerializeField] public AudioSource audioSource;

    [SerializeField] private string SubscriptionKey;
    [SerializeField] private string SubscriptionRegion;

    private Animator animator;
    private string message;

    private object threadLocker = new();
    private bool waitingForReco;
    private bool waitingForSpeak;
    private bool waitingForBot;

    private const int SampleRate = 24000;

    private SpeechConfig speechConfig;
    private SpeechSynthesizer synthesizer;
    private string detectedLanguage;

    private void Start()
    {
        if (OutputText == null)
        {
            Debug.LogError("outputText property is null! Assign a UI Text element to it.");
        }
        else if (startRecoButton == null)
        {
            message = "startRecoButton property is null! Assign a UI Button to it.";
            Debug.LogError(message);
        }
        else
        {
            // Continue with normal initialization, Text and Button objects are present.
            animator = Avatar.GetComponent<Animator>();

            // Creates an instance of a speech config with specified subscription key and service region.
            speechConfig = SpeechConfig.FromSubscription(SubscriptionKey, SubscriptionRegion);
            // set the desired language
            
            //speechConfig.SpeechSynthesisLanguage = "zh-TW";
            //Debug.Log("speech language:"+speechConfig.SpeechSynthesisLanguage);
            //speechConfig.SpeechSynthesisVoiceName = "zh-TW-HsiaoChenNeural"; // Set the desired voice , ex : zh-TW-YunJheNeural (Male)

            // The default format is RIFF, which has a riff header.
            // We are playing the audio in memory as audio clip, which doesn't require riff header.
            // So we need to set the format to raw (24KHz for better quality).
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);

            DirectLine.DirectLineConnection.instance.OnReceivedMessage += TextToSpeech;

            startRecoButton.onClick.AddListener(ButtonClick);

            // SendMessageToBot("Hello");
        }
    }

    // Update is called once per frame
    void Update()
    {
        lock (threadLocker)
        {
            if (startRecoButton != null)
            {
                startRecoButton.interactable = !waitingForReco && !waitingForSpeak && !waitingForBot;
            }
        }
    }

    public async void ButtonClick()
    {
        audioSource.Stop();
        string message = await SpeechToText();
        if (message != string.Empty)
        {
            waitingForBot = true;
            SendMessageToBot(message);
        }
    }

    public async Task<string> SpeechToText()
    {
        // Recognize once with At-start LID,set autodetect configuration for different languages
        var autoDetectSourceLanguageConfig =AutoDetectSourceLanguageConfig.FromLanguages(new string[] { "en-US", "ja-JP" ,"zh-TW" });
        
        // Creates an instance of a speech config with specified subscription key and service region.
        // Replace with your own subscription key and service region (e.g., "westus").
        var config = SpeechConfig.FromSubscription(SubscriptionKey, SubscriptionRegion);
        config.SpeechRecognitionLanguage = "zh-TW"; // Set the desired language, for example, Traditional Chinese (defaut:en_US)
  
        // Checks result.
        string newMessage = string.Empty;

        // Make sure to dispose the recognizer after use!
        using (var recognizer = new SpeechRecognizer(config,autoDetectSourceLanguageConfig))
        {
            lock (threadLocker)
            {
                waitingForReco = true;
                animator.SetTrigger("TrListen");
            }

            // Starts speech recognition, and returns after a single utterance is recognized. The end of a
            // single utterance is determined by listening for silence at the end or until a maximum of 15
            // seconds of audio is processed.  The task returns the recognition text as result.
            // Note: Since RecognizeOnceAsync() returns only a single utterance, it is suitable only for single
            // shot recognition like command or query.
            // For long-running multi-utterance recognition, use StartContinuousRecognitionAsync() instead.
            var result = await recognizer.RecognizeOnceAsync().ConfigureAwait(false);

            // Check the language code we get from speech NID , later use for tts speaker

            //var speechRecognitionResult = await recognizer.RecognizeOnceAsync();
            var autoDetectSourceLanguageResult = AutoDetectSourceLanguageResult.FromResult(result);
            detectedLanguage = autoDetectSourceLanguageResult.Language;
            
            Debug.Log("Language Detected :" + detectedLanguage);
            
            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                newMessage = result.Text;
            }
            else if (result.Reason == ResultReason.NoMatch)
            {
                Debug.LogWarning("NOMATCH: Speech could not be recognized.");
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = CancellationDetails.FromResult(result);
                Debug.LogWarning($"CANCELED: Reason={cancellation.Reason} ErrorDetails={cancellation.ErrorDetails}");
            }

            lock (threadLocker)
            {
                waitingForReco = false;
            }
        }

        return newMessage;
    }

    public void SendMessageToBot(string message)
    {

        DirectLine.DirectLineConnection.instance.SendMessage(message);

        // OnReceivedMessage => TextToSpeech;
    }


    private IEnumerator DetectLanguage(string inputText) 
    {
        string url = "https://a9language.cognitiveservices.azure.com" + "/text/analytics/v3.0/languages";
        string jsonInput = "{\"documents\": [{\"id\": \"1\", \"text\": \"" + inputText + "\"}]}";
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonInput);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Ocp-Apim-Subscription-Key", "bd61ab933bb84b63bd9cb897e224d0a0");

        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success) {
            Debug.Log(request.error);
        } else {
            Debug.Log(request.downloadHandler.text); // Parse the JSON response and extract the detected language and confidence score.
            //DocumentSet jsonResponse = JsonUtility.FromJson<DocumentSet>(request.downloadHandler.text);
            //Debug.Log("Detext Text Language :"+jsonResponse.documents[0].id);
            //Debug.Log("Detext Text Language :"+jsonResponse.documents[0].languageUsed.);
            JSONNode jsonResponse = JSON.Parse(request.downloadHandler.text);
            detectedLanguage = jsonResponse["documents"][0]["detectedLanguage"]["iso6391Name"];
            if(detectedLanguage=="en")
            detectedLanguage="en_US";
            else if(detectedLanguage == "ja")
            detectedLanguage = "ja_JP";
            else
            detectedLanguage = "zh_TW";
            Debug.Log($"Detected language: {detectedLanguage}");
            speechConfig.SpeechSynthesisLanguage = detectedLanguage;
        }

    }    

    public void TextToSpeech(string message)
    {
        if (message == "Welcome to Azure GPT Bot!" || message.Contains("Reach Token limit") )
        {
            return;
        }
        lock (threadLocker)
        {
            waitingForBot = false;
            waitingForSpeak = true;
            animator.SetTrigger("TrTalk");
        }
        speechConfig.SpeechSynthesisLanguage = detectedLanguage;
        Debug.Log("speech language:"+detectedLanguage);
        // Creates a speech synthesizer.
        // Make sure to dispose the synthesizer after use!
        StartCoroutine(DetectLanguage(message));
        Debug.Log("speech language:"+detectedLanguage);
        

        
        
        synthesizer = new SpeechSynthesizer(speechConfig, null);

        synthesizer.SynthesisCanceled += (s, e) =>
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(e.Result);
            message = $"CANCELED:\nReason=[{cancellation.Reason}]\nErrorDetails=[{cancellation.ErrorDetails}]\nDid you update the subscription info?";
        };
        
        // Starts speech synthesis, and returns once the synthesis is started.
        using (var result = synthesizer.StartSpeakingTextAsync(message).Result)
        {
            // Native playback is not supported on Unity yet (currently only supported on Windows/Linux Desktop).
            // Use the Unity API to play audio here as a short term solution.
            // Native playback support will be added in the future release.
            var audioDataStream = AudioDataStream.FromResult(result);
            var audioClip = AudioClip.Create(
                "Speech",
                SampleRate * 600, // Can speak 10mins audio as maximum
                1,
                SampleRate,
                true,
                (float[] audioChunk) =>
                {
                    var chunkSize = audioChunk.Length;
                    var audioChunkBytes = new byte[chunkSize * 2];
                    var readBytes = audioDataStream.ReadData(audioChunkBytes);

                    for (int i = 0; i < chunkSize; ++i)
                    {
                        if (i < readBytes / 2)
                        {
                            audioChunk[i] = (short)(audioChunkBytes[i * 2 + 1] << 8 | audioChunkBytes[i * 2]) / 32768.0F;
                        }
                        else
                        {
                            audioChunk[i] = 0.0f;
                        }
                    }
                });

            audioSource.PlayOneShot(audioClip);
        }

        lock (threadLocker)
        {
            waitingForSpeak = false;
        }
        
    }

    void OnDestroy()
    {
        if (synthesizer != null)
        {
            synthesizer.Dispose();
        }
    }

}
