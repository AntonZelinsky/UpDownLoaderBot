import asyncio
import logging
import os
import re
import traceback
from pathlib import Path

import instaloader
from dotenv import load_dotenv
from telegram import Update, ChatMemberAdministrator, ReactionTypeEmoji
from telegram.constants import ChatAction, ChatType, ChatMemberStatus, ReactionEmoji
from telegram.error import BadRequest
from telegram.ext import Application, CommandHandler, MessageHandler, filters, ContextTypes

from exceptions import ErrorDownload

logging.basicConfig(
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    level=logging.INFO
)
logger = logging.getLogger(__name__)

load_dotenv()
TOKEN = os.getenv('TELEGRAM_TOKEN')
if not TOKEN:
    raise ValueError("Не установлен TELEGRAM_TOKEN в файле .env")

TEMP_DIR = Path("temp_downloads")
TEMP_DIR.mkdir(exist_ok=True)

INSTAGRAM_REEL_PATTERN = re.compile(r'https?://(?:www\.)?instagram\.com/(?:[a-zA-Z0-9._-]+/)?reel/([a-zA-Z0-9_-]+)(?:/\?[^ ]*)?/?')

MAX_RETRIES = 3
RETRY_DELAY = 2

L = instaloader.Instaloader(
    download_videos=True,
    download_video_thumbnails=False,
    download_geotags=False,
    download_comments=False,
    save_metadata=False,
    compress_json=False,
    download_pictures=False,
    post_metadata_txt_pattern='',
    filename_pattern='{shortcode}'
)


async def start(update: Update, context: ContextTypes.DEFAULT_TYPE):
    await update.message.reply_text(
        'Привет! Я бот для скачивания видео из Instagram Reels. '
        'Просто отправь мне ссылку на Reels, и я скачаю его для тебя.'
    )


async def handle_message(update: Update, context: ContextTypes.DEFAULT_TYPE):
    effective_message = update.message or update.channel_post
    if not effective_message or not effective_message.text:
        return

    if not await check_bot_permissions(update, context):
        try:
            if update.effective_chat.type != ChatType.CHANNEL:
                await effective_message.reply_text(
                    "У бота недостаточно прав. Пожалуйста, предоставьте права на отправку сообщений и медиа.",
                    reply_to_message_id=effective_message.message_id
                )
        except BadRequest:
            pass
        return

    logger.info(f"Handling message `{effective_message.text}` from {effective_message.chat.full_name}")

    reel_matches = re.findall(INSTAGRAM_REEL_PATTERN, effective_message.text)
    is_link_only = INSTAGRAM_REEL_PATTERN.fullmatch(effective_message.text)

    for reel_id in reel_matches:
        try:
            await update.message.chat.send_chat_action(ChatAction.UPLOAD_VIDEO)

            video_path = await download_reel(reel_id)

            await update.message.chat.send_chat_action(ChatAction.UPLOAD_VIDEO)

            source_link = f"https://instagram.com/reel/{reel_id}"

            if is_link_only:
                await effective_message.chat.send_video(
                    video=open(video_path, 'rb'),
                    caption=source_link
                )
                await effective_message.delete()
            else:
                await effective_message.reply_video(
                    video=open(video_path, 'rb'),
                    reply_to_message_id=effective_message.message_id,
                    caption=source_link
                )

            logger.info(
                f"Send reel `{reel_id}` to `{effective_message.chat.effective_name}({effective_message.chat.id})` form `{effective_message.from_user.full_name}({effective_message.from_user.id})`")
        except Exception as e:
            logger.error(f"Ошибка при обработке Reels {reel_id}: {str(e)}")
            await effective_message.set_reaction(ReactionTypeEmoji(ReactionEmoji.THUMBS_DOWN))


async def check_bot_permissions(update: Update, context: ContextTypes.DEFAULT_TYPE) -> bool:
    try:
        chat = update.effective_chat
        if not chat:
            return False

        if chat.type == ChatType.PRIVATE:
            return True

        bot_member = await chat.get_member(context.bot.id)

        if chat.type == ChatType.SUPERGROUP and bot_member.status == ChatMemberStatus.ADMINISTRATOR:
            return True

        if isinstance(bot_member, ChatMemberAdministrator):
            required_rights = {
                "can_send_messages": True,
                "can_send_media_messages": True,
                "can_send_other_messages": True,
            }

            for right, required in required_rights.items():
                if not getattr(bot_member, right, False) and required:
                    return False
            return True
        elif bot_member.status == ChatMemberStatus.MEMBER:
            # Для обычного участника проверяем настройки группы
            return True

    except BadRequest as e:
        logger.error(f"Ошибка при проверке прав: {e}")
        return False


def clean_temp_directory():
    """Очистка директории с временными файлами при запуске"""
    try:
        for file in TEMP_DIR.glob('*'):
            try:
                file.unlink()
            except Exception as e:
                logger.error(f"Не удалось удалить файл {file}: {e}")
        logger.info("Временная директория очищена")
    except Exception as e:
        logger.error(f"Ошибка при очистке временной директории: {e}")


async def download_reel(shortcode: str) -> Path:
    retry_count = 1
    last_error = None

    while retry_count <= MAX_RETRIES:
        try:
            post = instaloader.Post.from_shortcode(L.context, shortcode)
            if not post.is_video:
                raise ValueError("Публикация не содержит видео")

            L.download_post(post, str(TEMP_DIR))

            video_path = TEMP_DIR / f'{shortcode}.mp4'
            check_file(video_path)
            logger.info(f"Download reel video `{shortcode}` to `{video_path}`")

            return video_path

        except Exception as e:
            last_error = f"Unexpected error: {str(e)} for {shortcode}"
            logger.error(f"Попытка {retry_count} не удалась: {last_error}. {traceback.format_exc()}")

        retry_count += 1
        if retry_count <= MAX_RETRIES:
            await asyncio.sleep(RETRY_DELAY)

    raise ErrorDownload(last_error, retry_count)


def check_file(video_path: Path):
    if not video_path.exists():
        raise FileNotFoundError("Файл видео не существует")

    file_size = video_path.stat().st_size
    if file_size == 0:
        raise ValueError("Загруженный файл пуст")


async def error_handler(update: Update, context: ContextTypes.DEFAULT_TYPE):
    logger.error(f"Update {update} caused error {context.error}")


def main():
    clean_temp_directory()

    application = Application.builder().token(TOKEN).media_write_timeout(60).connect_timeout(20).write_timeout(60).read_timeout(60).build()

    application.add_handler(CommandHandler("start", start))
    message_handler = MessageHandler(
        (filters.TEXT | filters.UpdateType.CHANNEL_POSTS | filters.ChatType.GROUPS | filters.ChatType.SUPERGROUP) & ~filters.COMMAND,
        handle_message
    )
    application.add_handler(message_handler)
    application.add_error_handler(error_handler)

    application.run_polling(allowed_updates=Update.ALL_TYPES)


if __name__ == '__main__':
    main()
