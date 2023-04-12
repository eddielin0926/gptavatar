using System;
using System.Threading;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Microsoft.CognitiveServices.Speech;
using System.Threading.Tasks;

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

            // The default format is RIFF, which has a riff header.
            // We are playing the audio in memory as audio clip, which doesn't require riff header.
            // So we need to set the format to raw (24KHz for better quality).
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);

            // Creates a speech synthesizer.
            // Make sure to dispose the synthesizer after use!
            synthesizer = new SpeechSynthesizer(speechConfig, null);

            synthesizer.SynthesisCanceled += (s, e) =>
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(e.Result);
                message = $"CANCELED:\nReason=[{cancellation.Reason}]\nErrorDetails=[{cancellation.ErrorDetails}]\nDid you update the subscription info?";
            };

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
        // Creates an instance of a speech config with specified subscription key and service region.
        // Replace with your own subscription key and service region (e.g., "westus").
        var config = SpeechConfig.FromSubscription(SubscriptionKey, SubscriptionRegion);
        // Checks result.
        string newMessage = string.Empty;

        // Make sure to dispose the recognizer after use!
        using (var recognizer = new SpeechRecognizer(config))
        {
            lock (threadLocker)
            {
                waitingForReco = true;
            }

            // Starts speech recognition, and returns after a single utterance is recognized. The end of a
            // single utterance is determined by listening for silence at the end or until a maximum of 15
            // seconds of audio is processed.  The task returns the recognition text as result.
            // Note: Since RecognizeOnceAsync() returns only a single utterance, it is suitable only for single
            // shot recognition like command or query.
            // For long-running multi-utterance recognition, use StartContinuousRecognitionAsync() instead.
            var result = await recognizer.RecognizeOnceAsync().ConfigureAwait(false);

            
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

    public void TextToSpeech(string message)
    {
        if (message == "Welcome to Azure GPT Bot!" || message == "Reset dialog history and Restart")
        {
            return;
        }
        lock (threadLocker)
        {
            waitingForBot = false;
            waitingForSpeak = true;
        }

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
