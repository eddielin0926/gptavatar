using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BotService : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI Text;
    [SerializeField] private Button btnPost;
    [SerializeField] private TextMeshProUGUI textChat;

    private void Start()
    {
        btnPost.onClick.AddListener(delegate {
            DirectLine.DirectLineConnection.instance.SendMessage(Text.text);
        });

        DirectLine.DirectLineConnection.instance.OnReceivedMessage += AppendBotChatMessage;
    }

    private void AppendBotChatMessage(string message)
    {
        textChat.text = message;
    }
}
