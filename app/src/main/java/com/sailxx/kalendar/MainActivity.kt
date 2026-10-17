package com.sailxx.kalendar

import android.app.Activity
import android.app.TimePickerDialog
import android.content.Intent
import android.net.Uri
import android.os.PowerManager
import android.provider.Settings as AndroidSettings
import android.graphics.Color
import android.graphics.Outline
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.util.DisplayMetrics
import android.view.Gravity
import android.view.View
import android.view.ViewOutlineProvider
import android.view.WindowInsets
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

class MainActivity : Activity() {
    private lateinit var s: Settings
    private lateinit var preview: ImageView
    private lateinit var status: TextView
    private lateinit var stop: TextView
    private val dp get() = resources.displayMetrics.density

    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        s = Settings.of(this)
        saveScreenSize()
        window.statusBarColor = BG
        window.navigationBarColor = BG

        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(px(20), px(16), px(20), px(24))
        }
        val scroll = ScrollView(this).apply {
            setBackgroundColor(BG)
            addView(col)
            setOnApplyWindowInsetsListener { v, ins ->
                val t: Int; val b: Int
                if (Build.VERSION.SDK_INT >= 30) {
                    val i = ins.getInsets(WindowInsets.Type.systemBars())
                    t = i.top; b = i.bottom
                } else {
                    @Suppress("DEPRECATION")
                    t = ins.systemWindowInsetTop
                    @Suppress("DEPRECATION")
                    b = ins.systemWindowInsetBottom
                }
                v.setPadding(0, t, 0, b)
                ins
            }
        }

        col.addView(TextView(this).apply {
            text = "Календарь-обои"
            textSize = 26f
            typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
            setTextColor(Color.WHITE)
        })
        col.addView(TextView(this).apply {
            text = "Год точками на экране. Обновляется сам каждый день, тему можно менять по расписанию."
            textSize = 14f
            setTextColor(DIM)
            setPadding(0, px(4), 0, px(16))
        })

        val (w, h) = Wallpaper.screen(this)
        preview = ImageView(this).apply {
            scaleType = ImageView.ScaleType.FIT_CENTER
            outlineProvider = object : ViewOutlineProvider() {
                override fun getOutline(v: View, o: Outline) = o.setRoundRect(0, 0, v.width, v.height, 22 * dp)
            }
            clipToOutline = true
        }
        val pw = (resources.displayMetrics.widthPixels * 0.62f).toInt()
        col.addView(preview, LinearLayout.LayoutParams(pw, pw * h / w).apply { gravity = Gravity.CENTER_HORIZONTAL })

        val themes = listOf("Чёрная", "Красная", "Светлая")
        val one = group(col, "Тема", themes, { s.theme }) { s.theme = it }
        val day = group(col, "Днём", themes, { s.dayTheme }) { s.dayTheme = it }
        val night = group(col, "Ночью", themes, { s.nightTheme }) { s.nightTheme = it }
        val times = timeRow(col)
        fun show() {
            one.visibility = if (s.schedule == 0) View.VISIBLE else View.GONE
            // День и ночь выбираются только для смены по времени; «Система» берёт тему телефона.
            times.visibility = if (s.schedule == 2) View.VISIBLE else View.GONE
            day.visibility = times.visibility
            night.visibility = times.visibility
        }
        val mode = group(col, "Смена темы", listOf("Нет", "Система", "По времени"), { s.schedule }) {
            s.schedule = it
            show()
        }
        // «Смена темы» — над выбором тем.
        col.removeView(mode)
        col.addView(mode, col.indexOfChild(one))
        show()
        group(col, "Размер", listOf("S", "M", "L"), { s.size }) { s.size = it }
        group(col, "Где на экране", listOf("Под часами", "По центру", "Внизу"), { s.place }) { s.place = it }
        group(col, "Подписи", listOf("Русские", "English"), { s.lang }) { s.lang = it }
        group(col, "Ставить на", listOf("Блокировку", "Рабочий стол", "Оба"), { s.target }) { s.target = it }

        col.addView(button("Поставить обои", primary = true) { applyNow() }, LinearLayout.LayoutParams(-1, px(52)).apply { topMargin = px(28) })
        stop = button("Выключить автообновление", primary = false) {
            s.enabled = 0
            Wallpaper.cancel(this)
            refresh()
        }
        col.addView(stop, LinearLayout.LayoutParams(-1, px(48)).apply { topMargin = px(10) })
        status = TextView(this).apply {
            textSize = 13f
            setTextColor(DIM)
            gravity = Gravity.CENTER
            setPadding(0, px(14), 0, 0)
        }
        col.addView(status)

        setContentView(scroll)
        refresh()
    }

    // При открытии (и после смены темы системы — экран пересоздаётся) сразу подгоняем обои.
    override fun onResume() {
        super.onResume()
        if (s.enabled == 0) return
        Thread {
            runCatching { Wallpaper.apply(this, force = false) }
            Wallpaper.schedule(this)
        }.start()
    }

    private fun applyNow(toast: Boolean = true) {
        refresh()
        Thread {
            val ok = runCatching { Wallpaper.apply(this) }.isSuccess
            runOnUiThread {
                if (ok) {
                    s.enabled = 1
                    Wallpaper.schedule(this)
                    if (toast) Toast.makeText(this, "Готово", Toast.LENGTH_SHORT).show()
                    askBattery()
                } else {
                    Toast.makeText(this, "Не удалось поставить обои", Toast.LENGTH_LONG).show()
                }
                refresh()
            }
        }.start()
    }

    private fun refresh() {
        val (w, h) = Wallpaper.screen(this)
        preview.setImageBitmap(Wallpaper.render(this, w / 3, h / 3))
        val on = s.enabled == 1
        val now = listOf("чёрная", "красная", "светлая")[Wallpaper.theme(this)]
        val sys = if (Wallpaper.systemNight(this)) "тёмная" else "светлая"
        status.text = (if (on) "Автообновление включено" else "Автообновление выключено — нажми «Поставить обои»") +
            "\nСейчас на обоях: $now" + (if (s.schedule == 1) " · тема системы: $sys" else "") + lastRun() +
            (if (on && !batteryFree()) "\nДля надёжного обновления сними ограничение батареи для приложения" else "")
        stop.visibility = if (on) View.VISIBLE else View.GONE
    }

    private fun lastRun(): String {
        if (s.lastRun == 0L) return ""
        val t = Instant.ofEpochMilli(s.lastRun).atZone(ZoneId.systemDefault())
        val day = when (t.toLocalDate()) {
            LocalDate.now() -> "сегодня"
            LocalDate.now().minusDays(1) -> "вчера"
            else -> "%02d.%02d".format(t.dayOfMonth, t.monthValue)
        }
        return "\nОбновлено: $day в %02d:%02d".format(t.hour, t.minute)
    }

    private fun batteryFree() = getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(packageName)

    // Без ограничений батареи Android не откладывает обновление в полночь. Спрашиваем один раз после «Поставить обои».
    private fun askBattery() {
        if (batteryFree() || s.askedBattery == 1) return
        s.askedBattery = 1
        runCatching {
            startActivity(Intent(AndroidSettings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName")))
        }
    }

    private fun caption(box: LinearLayout, title: String) = box.addView(TextView(this).apply {
        text = title
        textSize = 13f
        setTextColor(DIM)
        setPadding(px(2), px(18), 0, px(8))
    })

    private fun chipStyle(v: TextView, on: Boolean) {
        v.background = GradientDrawable().apply {
            cornerRadius = 21 * dp
            setColor(if (on) Color.WHITE else CHIP)
        }
        v.setTextColor(if (on) Color.BLACK else Color.WHITE)
    }

    // Любая настройка сразу ставит обои (для «Система» — по текущей теме телефона).
    private fun changed() = applyNow(toast = false)

    // Время смены тем: два поля, по нажатию — выбор часов и минут.
    private fun timeRow(col: LinearLayout): LinearLayout {
        val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        caption(box, "Расписание")
        val row = LinearLayout(this)
        fun field(label: String, get: () -> Int, set: (Int) -> Unit) = TextView(this).apply {
            textSize = 14f
            gravity = Gravity.CENTER
            isSingleLine = true
            chipStyle(this, false)
            val show = { text = "%s с %02d:%02d".format(label, get() / 60, get() % 60) }
            show()
            setOnClickListener {
                TimePickerDialog(this@MainActivity, { _, h, m ->
                    set(h * 60 + m)
                    show()
                    changed()
                }, get() / 60, get() % 60, true).show()
            }
        }
        row.addView(field("День", { s.dayAt }) { s.dayAt = it }, LinearLayout.LayoutParams(0, px(42), 1f))
        row.addView(field("Ночь", { s.nightAt }) { s.nightAt = it }, LinearLayout.LayoutParams(0, px(42), 1f).apply { marginStart = px(8) })
        box.addView(row)
        col.addView(box)
        return box
    }

    private fun group(col: LinearLayout, title: String, items: List<String>, get: () -> Int, set: (Int) -> Unit): LinearLayout {
        val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        caption(box, title)
        val row = LinearLayout(this)
        val chips = items.mapIndexed { i, label ->
            TextView(this).apply {
                text = label
                textSize = 14f
                gravity = Gravity.CENTER
                isSingleLine = true
                setOnClickListener {
                    set(i)
                    restyle(row, get())
                    // Если автообновление уже включено — сразу переставляем обои.
                    changed()
                }
            }
        }
        chips.forEachIndexed { i, v ->
            row.addView(v, LinearLayout.LayoutParams(0, px(42), 1f).apply { if (i > 0) marginStart = px(8) })
        }
        restyle(row, get())
        box.addView(row)
        col.addView(box)
        return box
    }

    private fun restyle(row: LinearLayout, sel: Int) {
        for (i in 0 until row.childCount) chipStyle(row.getChildAt(i) as TextView, i == sel)
    }

    private fun button(label: String, primary: Boolean, click: () -> Unit) = TextView(this).apply {
        text = label
        textSize = 16f
        gravity = Gravity.CENTER
        typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
        setTextColor(if (primary) Color.WHITE else DIM)
        background = GradientDrawable().apply {
            cornerRadius = 26 * dp
            setColor(if (primary) ACCENT else CHIP)
        }
        setOnClickListener { click() }
    }

    private fun saveScreenSize() {
        val w: Int; val h: Int
        if (Build.VERSION.SDK_INT >= 30) {
            val b = windowManager.maximumWindowMetrics.bounds
            w = b.width(); h = b.height()
        } else {
            val m = DisplayMetrics()
            @Suppress("DEPRECATION")
            windowManager.defaultDisplay.getRealMetrics(m)
            w = m.widthPixels; h = m.heightPixels
        }
        s.width = minOf(w, h)
        s.height = maxOf(w, h)
    }

    private fun px(v: Int) = (v * dp).toInt()

    companion object {
        private const val BG = 0xFF0E0E10.toInt()
        private const val CHIP = 0xFF26262A.toInt()
        private const val DIM = 0xFF9A9AA0.toInt()
        private const val ACCENT = 0xFFFF5A1F.toInt()
    }
}
