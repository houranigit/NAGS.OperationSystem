package com.nags.operations.ui.components

import androidx.compose.foundation.Image
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import com.nags.operations.R
import com.nags.operations.ui.theme.BrandRed

/** NAGS portal mark on a transparent background. Tinted to the requested color. */
@Composable
fun NagsLogo(
    modifier: Modifier = Modifier,
    tint: Color = BrandRed,
    contentScale: ContentScale = ContentScale.Fit,
) {
    Image(
        painter = painterResource(id = R.drawable.nags_logo),
        contentDescription = "NAGS",
        modifier = modifier,
        colorFilter = ColorFilter.tint(tint),
        contentScale = contentScale,
    )
}
