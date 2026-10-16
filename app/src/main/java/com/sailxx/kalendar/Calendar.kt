package com.sailxx.kalendar

import android.app.AlarmManager
import android.app.PendingIntent
import android.app.WallpaperManager
import android.app.job.JobInfo
import android.app.job.JobParameters
import android.app.job.JobScheduler
import android.app.job.JobService
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Typeface
import android.content.res.Configuration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import kotlin.reflect.KProperty

// Настройки: тема, размер, положение, язык подписей, куда ставить.
class Settings(private val p: SharedPreferences) {
    var theme by Pref(p, "theme", 0)      // 0 чёрная, 1 красная, 2 светлая
    var schedule by Pref(p, "schedule", 0) // 0 одна тема, 1 как в системе, 2 по времени
    var dayTheme by Pref(p, "dayTheme", 2)
    var nightTheme by Pref(p, "nightTheme", 0)
    var dayAt by Pref(p, "dayAt", 7 * 60)     // минуты от полуночи
    var nightAt by Pref(p, "nightAt", 21 * 60)
    var size by Pref(p, "size", 1)        // 0 S, 1 M, 2 L
    var place by Pref(p, "place", 0)      // 0 ниже часов, 1 по центру, 2 внизу
    var lang by Pref(p, "lang", 0)        // 0 рус, 1 eng
    var target by Pref(p, "target", 0)    // 0 блокировка, 1 рабочий стол, 2 оба
    var enabled by Pref(p, "enabled", 0)
    var width by Pref(p, "width", 0)
    var height by Pref(p, "height", 0)

    // Что стоит на экране сейчас: «дата|тема». Будильник не переставляет обои зря.
    var applied: String
        get() = p.getString("applied", "") ?: ""
        set(v) = p.edit().putString("applied", v).apply()

    companion object {
        fun of(ctx: Context) = Settings(ctx.getSharedPreferences("kalendar", Context.MODE_PRIVATE))
    }
}

class Pref(private val p: SharedPreferences, private val key: String, private val def: Int) {
    operator fun getValue(o: Any, prop: KProperty<*>): Int = p.getInt(key, def)
    operator fun setValue(o: Any, prop: KProperty<*>, v: Int) = p.edit().putInt(key, v).apply()
}

private class Palette(val bg: Int, val past: Int, val today: Int, val future: Int, val label: Int)

private val palettes = listOf(
    Palette(0xFF1C1C1E.toInt(), 0xFFF2F2F2.toInt(), 0xFFFF5A1F.toInt(), 0x38FFFFFF, 0x99FFFFFF.toInt()),
    Palette(0xFFF2380F.toInt(), 0xFFFFD36B.toInt(), 0xFFFFFFFF.toInt(), 0x40000000, 0xB3FFE7B0.toInt()),
    Palette(0xFFE6E6E6.toInt(), 0xFF262626.toInt(), 0xFFFF5A1F.toInt(), 0x2E000000, 0x99262626.toInt()),
)

private val monthsRu = listOf("Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек")
private val monthsEn = listOf("Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec")

object Wallpaper {
    fun screen(ctx: Context): Pair<Int, Int> {
        val s = Settings.of(ctx)
        if (s.width > 0 && s.height > 0) return s.width to s.height
        val m = ctx.resources.displayMetrics
        return minOf(m.widthPixels, m.heightPixels) to maxOf(m.widthPixels, m.heightPixels)
    }

    private fun isNight(ctx: Context, s: Settings, now: LocalDateTime): Boolean = when (s.schedule) {
        1 -> ctx.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES
        2 -> {
            val t = now.hour * 60 + now.minute
            if (s.dayAt <= s.nightAt) t !in s.dayAt until s.nightAt
            else t in s.nightAt until s.dayAt
        }
        else -> false
    }

    fun theme(ctx: Context, now: LocalDateTime = LocalDateTime.now()): Int {
        val s = Settings.of(ctx)
        val t = when (s.schedule) {
            0 -> s.theme
            // «Система»: тёмная тема телефона — чёрные обои, светлая — светлые.
            1 -> if (isNight(ctx, s, now)) 0 else 2
            else -> if (isNight(ctx, s, now)) s.nightTheme else s.dayTheme
        }
        return t.coerceIn(0, 2)
    }

    fun render(ctx: Context, w: Int, h: Int, date: LocalDate = LocalDate.now()): Bitmap {
        val s = Settings.of(ctx)
        val pal = palettes[theme(ctx)]
        val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        c.drawColor(pal.bg)

        // Сетка 3×4 месяца, в каждом точки по 7 в ряд (до 5 рядов).
        val gridW = w * listOf(0.58f, 0.68f, 0.80f)[s.size.coerceIn(0, 2)]
        val colGap = gridW * 0.07f
        val monthW = (gridW - 2 * colGap) / 3
        val pitch = monthW / 7
        val r = pitch * 0.30f
        val labelH = pitch * 1.5f
        val rowGap = pitch * 1.1f
        val monthH = labelH + 5 * pitch
        val footerH = pitch * 2.6f
        val gridH = 4 * monthH + 3 * rowGap + footerH

        val top = when (s.place) {
            1 -> (h - gridH) / 2
            2 -> h * 0.86f - gridH
            else -> h * 0.40f
        }
        val left = (w - gridW) / 2

        val dot = Paint(Paint.ANTI_ALIAS_FLAG)
        val text = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
            textSize = pitch * 0.78f
        }
        val names = if (s.lang == 1) monthsEn else monthsRu
        val year = date.year

        for (m in 0 until 12) {
            val x0 = left + (m % 3) * (monthW + colGap)
            val y0 = top + (m / 3) * (monthH + rowGap)
            text.color = if (m + 1 == date.monthValue) pal.past else pal.label
            c.drawText(names[m], x0 + pitch / 2 - r, y0 + text.textSize, text)

            val days = LocalDate.of(year, m + 1, 1).lengthOfMonth()
            for (d in 1..days) {
                val day = LocalDate.of(year, m + 1, d)
                dot.color = when {
                    day == date -> pal.today
                    day.isBefore(date) -> pal.past
                    else -> pal.future
                }
                val i = d - 1
                c.drawCircle(x0 + (i % 7) * pitch + pitch / 2, y0 + labelH + (i / 7) * pitch + pitch / 2, r, dot)
            }
        }

        // Подпись: сколько дней осталось и процент года.
        val len = date.lengthOfYear()
        val left2 = len - date.dayOfYear
        val pct = date.dayOfYear * 100 / len
        val a = if (s.lang == 1) "${left2}d left" else "осталось $left2 дн."
        val b = "  ·  $pct%"
        text.textSize = pitch * 0.85f
        val wa = text.measureText(a)
        val wb = text.measureText(b)
        val ty = top + gridH - pitch * 0.6f
        var tx = (w - wa - wb) / 2
        text.color = pal.today
        c.drawText(a, tx, ty, text)
        tx += wa
        text.color = pal.label
        c.drawText(b, tx, ty, text)
        return bmp
    }

    fun apply(ctx: Context, force: Boolean = true) {
        val s = Settings.of(ctx)
        val key = "${LocalDate.now()}|${theme(ctx)}"
        if (!force && key == s.applied) return
        val (w, h) = screen(ctx)
        val bmp = render(ctx, w, h)
        val flags = when (s.target) {
            1 -> WallpaperManager.FLAG_SYSTEM
            2 -> WallpaperManager.FLAG_SYSTEM or WallpaperManager.FLAG_LOCK
            else -> WallpaperManager.FLAG_LOCK
        }
        WallpaperManager.getInstance(ctx).setBitmap(bmp, null, true, flags)
        bmp.recycle()
        s.applied = key
    }

    private fun alarm(ctx: Context) = PendingIntent.getBroadcast(
        ctx, 0, Intent(ctx, UpdateReceiver::class.java).setAction(UpdateReceiver.UPDATE),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    // Ближайший будильник: полночь, смена темы по времени или проверка темы системы раз в 5 минут.
    // После срабатывания ставится снова.
    fun schedule(ctx: Context) {
        val s = Settings.of(ctx)
        val zone = ZoneId.systemDefault()
        val now = LocalDateTime.now(zone)
        val times = mutableListOf(now.toLocalDate().plusDays(1).atStartOfDay())
        when (s.schedule) {
            1 -> times += now.plusMinutes(5)
            2 -> for (m in listOf(s.dayAt, s.nightAt)) {
                var t = now.toLocalDate().atStartOfDay().plusMinutes(m.toLong())
                if (!t.isAfter(now)) t = t.plusDays(1)
                times += t
            }
        }
        val at = times.min().atZone(zone).toInstant().toEpochMilli() + 5_000
        ctx.getSystemService(AlarmManager::class.java)
            .setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, alarm(ctx))
        watchTheme(ctx)
    }

    fun cancel(ctx: Context) {
        ctx.getSystemService(AlarmManager::class.java).cancel(alarm(ctx))
        ctx.getSystemService(JobScheduler::class.java).cancel(THEME_JOB)
    }

    // Режим «Система»: задача срабатывает сразу, как только в системе переключают тёмную тему.
    // Будильник раз в 5 минут остаётся запасным вариантом.
    private fun watchTheme(ctx: Context) {
        val js = ctx.getSystemService(JobScheduler::class.java)
        val s = Settings.of(ctx)
        if (s.schedule != 1 || s.enabled == 0) {
            js.cancel(THEME_JOB)
            return
        }
        val b = JobInfo.Builder(THEME_JOB, ComponentName(ctx, ThemeJob::class.java))
        for (key in listOf("ui_night_mode", "ui_night_mode_override_on", "ui_night_mode_override_off")) {
            b.addTriggerContentUri(JobInfo.TriggerContentUri(android.provider.Settings.Secure.getUriFor(key), 0))
        }
        // Небольшая пауза: настройка записывается чуть раньше, чем приложения получают новую тему.
        b.setTriggerContentUpdateDelay(1_500).setTriggerContentMaxDelay(5_000)
        js.schedule(b.build())
    }

    private const val THEME_JOB = 1
}

class ThemeJob : JobService() {
    override fun onStartJob(params: JobParameters): Boolean {
        Thread {
            try {
                if (Settings.of(this).enabled == 1) Wallpaper.apply(this, force = false)
            } catch (_: Exception) {
            } finally {
                // Задача с триггером одноразовая — ставим снова.
                Wallpaper.schedule(this)
                jobFinished(params, false)
            }
        }.start()
        return true
    }

    override fun onStopJob(params: JobParameters) = false
}

class UpdateReceiver : BroadcastReceiver() {
    override fun onReceive(ctx: Context, intent: Intent) {
        if (Settings.of(ctx).enabled == 0) return
        val pending = goAsync()
        Thread {
            try {
                Wallpaper.apply(ctx, force = intent.action != UPDATE)
            } catch (_: Exception) {
            } finally {
                Wallpaper.schedule(ctx)
                pending.finish()
            }
        }.start()
    }

    companion object {
        const val UPDATE = "com.sailxx.kalendar.UPDATE"
    }
}
