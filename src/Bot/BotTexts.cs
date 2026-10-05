namespace UpDownLoaderBot.Bot;

/// <summary>Everything the bot says to a user. Code, comments and logs stay English.</summary>
public static class BotTexts
{
    /// <summary>The reply to <c>/start</c> — the button Telegram shows on the first visit.</summary>
    public static readonly LocalizedText StartInstructions = new(
        belarusian:
        """
        Прывітанне! Я ўмею спампоўваць відэа з Instagram, TikTok і Facebook.

        Каб атрымаць відэа, дашліце мне спасылку на яго — Reels, пост з відэа або старую
        IGTV-спасылку (instagram.com/reel/…, /p/…, /tv/…), відэа з TikTok
        (tiktok.com/@…/video/…, кароткія vm.tiktok.com/… таксама падыдуць) альбо відэа ці Reels
        з Facebook (facebook.com/share/r/…, /reel/…, fb.watch/…). У адказ на гэтую спасылку
        прыйдзе само відэа.

        Фотаздымкі я не спампоўваю — ні пост з фота ў Instagram, ні фотаслайдшоу ў TikTok.
        З поста Instagram з некалькімі відэа дашлю першае.

        Працую ў асабістых паведамленнях, групах і каналах. У групе мяне трэба зрабіць
        адміністратарам — без гэтага Telegram не паказвае мне звычайныя паведамленні, і спасылку
        я не пабачу. Дадатковыя правы выдаваць не трэба, дастаткова магчымасці надсылаць
        паведамленні.

        Калі на спасылцы з'явілася рэакцыя 😴 — значыць спампаваць гэтае відэа і даслаць яго
        ў чат не атрымалася.
        """,
        english:
        """
        Hi! I download videos from Instagram, TikTok and Facebook.

        Send me a link to one — an Instagram Reel, a post with a video or an old IGTV link
        (instagram.com/reel/…, /p/…, /tv/…), a TikTok video (tiktok.com/@…/video/…, and the
        short vm.tiktok.com/… links too), or a Facebook video or reel (facebook.com/share/r/…,
        /reel/…, fb.watch/…). The video comes back as a reply to that link.

        I don't download photos — neither an Instagram photo post nor a TikTok photo slideshow.
        From an Instagram post with several videos I send the first one.

        I work in direct messages, groups and channels. In a group I have to be an administrator —
        without that Telegram doesn't show me ordinary messages and I never see the link. No extra
        rights are needed, permission to send messages is enough.

        A 😴 reaction on your link means downloading that video and sending it to the chat
        didn't work out.
        """,
        polish:
        """
        Cześć! Pobieram filmy z Instagrama, TikToka i Facebooka.

        Wyślij mi link do filmu — Reels, post z filmem albo stary link IGTV
        (instagram.com/reel/…, /p/…, /tv/…), film z TikToka (tiktok.com/@…/video/…, krótkie
        linki vm.tiktok.com/… też się nadają) lub film albo rolkę z Facebooka
        (facebook.com/share/r/…, /reel/…, fb.watch/…). Film wróci w odpowiedzi na ten link.

        Zdjęć nie pobieram — ani posta ze zdjęciami na Instagramie, ani pokazu slajdów na TikToku.
        Z posta na Instagramie z kilkoma filmami wyślę pierwszy.

        Działam w wiadomościach prywatnych, grupach i kanałach. W grupie muszę być
        administratorem — bez tego Telegram nie pokazuje mi zwykłych wiadomości i nie zobaczę
        linku. Dodatkowe uprawnienia nie są potrzebne, wystarczy możliwość wysyłania wiadomości.

        Reakcja 😴 na linku oznacza, że nie udało się pobrać tego filmu i wysłać go na czat.
        """,
        russian:
        """
        Привет! Я умею скачивать видео из Instagram, TikTok и Facebook.

        Чтобы получить видео, пришлите мне ссылку на него — Reels, пост с видео или старую
        IGTV-ссылку (instagram.com/reel/…, /p/…, /tv/…), видео из TikTok
        (tiktok.com/@…/video/…, короткие vm.tiktok.com/… тоже подойдут) либо видео или Reels
        из Facebook (facebook.com/share/r/…, /reel/…, fb.watch/…). В ответ на эту ссылку
        придёт само видео.

        Фотографии я не скачиваю — ни пост с фото в Instagram, ни фотослайдшоу в TikTok.
        Из поста Instagram с несколькими видео пришлю первое.

        Работаю в личных сообщениях, группах и каналах. В группе меня нужно сделать
        администратором — без этого Telegram не показывает мне обычные сообщения, и ссылку я не увижу.
        Дополнительные права выдавать не нужно, достаточно возможности отправлять сообщения.

        Если на ссылке появилась реакция 😴 — значит скачать это видео и прислать его в чат
        не получилось.
        """,
        ukrainian:
        """
        Привіт! Я вмію завантажувати відео з Instagram, TikTok і Facebook.

        Щоб отримати відео, надішліть мені посилання на нього — Reels, пост із відео або старе
        IGTV-посилання (instagram.com/reel/…, /p/…, /tv/…), відео з TikTok
        (tiktok.com/@…/video/…, короткі vm.tiktok.com/… теж підійдуть) чи відео або Reels
        з Facebook (facebook.com/share/r/…, /reel/…, fb.watch/…). У відповідь на це посилання
        прийде саме відео.

        Фотографії я не завантажую — ні пост із фото в Instagram, ні фотослайдшоу в TikTok.
        З поста Instagram з кількома відео надішлю перше.

        Працюю в особистих повідомленнях, групах і каналах. У групі мене потрібно зробити
        адміністратором — без цього Telegram не показує мені звичайні повідомлення, і посилання
        я не побачу. Додаткові права видавати не потрібно, достатньо можливості надсилати
        повідомлення.

        Якщо на посиланні з'явилася реакція 😴 — значить завантажити це відео та надіслати його
        в чат не вдалося.
        """);
}
