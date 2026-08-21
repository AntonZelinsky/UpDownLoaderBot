namespace UpDownLoaderBot.Bot;

/// <summary>Everything the bot says to a user. Code, comments and logs stay English.</summary>
public static class BotTexts
{
    /// <summary>The reply to <c>/start</c> — the button Telegram shows on the first visit.</summary>
    public static readonly LocalizedText StartInstructions = new(
        belarusian:
        """
        Прывітанне! Я ўмею спампоўваць відэа з Instagram.

        Каб атрымаць відэа, дашліце мне спасылку на яго — Reels, пост з відэа або старую
        IGTV-спасылку (instagram.com/reel/…, /p/…, /tv/…). У адказ на гэтую спасылку прыйдзе
        само відэа. Фотаздымкі я не спампоўваю; з поста з некалькімі відэа дашлю першае.

        Працую ў асабістых паведамленнях, групах і каналах. У групе мяне трэба зрабіць
        адміністратарам — без гэтага Telegram не паказвае мне звычайныя паведамленні, і спасылку
        я не пабачу. Дадатковыя правы выдаваць не трэба, дастаткова магчымасці надсылаць
        паведамленні.

        Калі на спасылцы з'явілася рэакцыя 👎 — значыць спампаваць гэтае відэа і даслаць яго
        ў чат не атрымалася.
        """,
        english:
        """
        Hi! I download videos from Instagram.

        Send me a link to one — a Reel, a post with a video or an old IGTV link
        (instagram.com/reel/…, /p/…, /tv/…), and the video comes back as a reply to that link.
        I don't download photos; from a post with several videos I send the first one.

        I work in direct messages, groups and channels. In a group I have to be an administrator —
        without that Telegram doesn't show me ordinary messages and I never see the link. No extra
        rights are needed, permission to send messages is enough.

        A 👎 reaction on your link means downloading that video and sending it to the chat
        didn't work out.
        """,
        polish:
        """
        Cześć! Pobieram filmy z Instagrama.

        Wyślij mi link do filmu — Reels, post z filmem albo stary link IGTV
        (instagram.com/reel/…, /p/…, /tv/…). Film wróci w odpowiedzi na ten link. Zdjęć nie
        pobieram; z posta z kilkoma filmami wyślę pierwszy.

        Działam w wiadomościach prywatnych, grupach i kanałach. W grupie muszę być
        administratorem — bez tego Telegram nie pokazuje mi zwykłych wiadomości i nie zobaczę
        linku. Dodatkowe uprawnienia nie są potrzebne, wystarczy możliwość wysyłania wiadomości.

        Reakcja 👎 na linku oznacza, że nie udało się pobrać tego filmu i wysłać go na czat.
        """,
        russian:
        """
        Привет! Я умею скачивать видео из Instagram.

        Чтобы получить видео, пришлите мне ссылку на него — Reels, пост с видео или старую
        IGTV-ссылку (instagram.com/reel/…, /p/…, /tv/…). В ответ на эту ссылку придёт само видео.
        Фотографии я не скачиваю; из поста с несколькими видео пришлю первое.

        Работаю в личных сообщениях, группах и каналах. В группе меня нужно сделать
        администратором — без этого Telegram не показывает мне обычные сообщения, и ссылку я не увижу.
        Дополнительные права выдавать не нужно, достаточно возможности отправлять сообщения.

        Если на ссылке появилась реакция 👎 — значит скачать это видео и прислать его в чат
        не получилось.
        """,
        ukrainian:
        """
        Привіт! Я вмію завантажувати відео з Instagram.

        Щоб отримати відео, надішліть мені посилання на нього — Reels, пост із відео або старе
        IGTV-посилання (instagram.com/reel/…, /p/…, /tv/…). У відповідь на це посилання прийде
        саме відео. Фотографії я не завантажую; з поста з кількома відео надішлю перше.

        Працюю в особистих повідомленнях, групах і каналах. У групі мене потрібно зробити
        адміністратором — без цього Telegram не показує мені звичайні повідомлення, і посилання
        я не побачу. Додаткові права видавати не потрібно, достатньо можливості надсилати
        повідомлення.

        Якщо на посиланні з'явилася реакція 👎 — значить завантажити це відео та надіслати його
        в чат не вдалося.
        """);
}
