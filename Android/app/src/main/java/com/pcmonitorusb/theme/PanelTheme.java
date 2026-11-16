package com.pcmonitorusb.theme;

import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

public final class PanelTheme {
    private final AccentPalette palette;
    private final float density;

    public PanelTheme(AccentPalette palette, float density) {
        this.palette = palette;
        this.density = density;
    }

    public void apply(View view) {
        Object tag = view.getTag();
        if ("theme_surface".equals(tag)) view.setBackground(shape(palette.surface, palette.line, 10));
        if ("theme_line".equals(tag)) view.setBackgroundColor(palette.line);
        if ("theme_accent".equals(tag) && view instanceof TextView)
            ((TextView) view).setTextColor(palette.accent);
        if (view instanceof ViewGroup) {
            ViewGroup group = (ViewGroup) view;
            for (int i = 0; i < group.getChildCount(); i++) apply(group.getChildAt(i));
        }
    }

    public void button(View view, boolean wake) {
        StateListDrawable states = new StateListDrawable();
        states.addState(new int[] {android.R.attr.state_pressed},
                shape(palette.pressed, palette.accent, wake ? 18 : 8));
        states.addState(new int[0], shape(wake ? palette.selected : palette.raised,
                wake ? palette.accent : palette.line, wake ? 18 : 8));
        view.setBackground(states);
    }

    public void tab(TextView view, boolean selected) {
        view.setTextColor(selected ? palette.accent : 0xffa9afb8);
        view.setBackground(shape(selected ? palette.selected : palette.surface,
                selected ? palette.accent : palette.line, 9));
    }

    private GradientDrawable shape(int fill, int border, int radius) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(fill);
        drawable.setCornerRadius(radius * density);
        drawable.setStroke(Math.max(1, Math.round(density)), border);
        return drawable;
    }
}
