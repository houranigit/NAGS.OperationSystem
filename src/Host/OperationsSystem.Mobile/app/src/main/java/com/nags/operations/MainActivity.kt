package com.nags.operations

import android.os.Bundle
import android.content.Intent
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import com.nags.operations.ui.OperationsApp
import com.nags.operations.ui.theme.OperationsTheme
import com.nags.operations.notifications.SystemNotificationManager

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        super.onCreate(savedInstanceState)
        AppGraph.get(applicationContext).notificationNavigation.acceptIntent(intent)
        SystemNotificationManager(applicationContext).ensureChannel()

        // Transparent scrim + light status icons so clocks/notifications read on red headers.
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.light(
                Color.Transparent.toArgb(),
                Color.Transparent.toArgb(),
            ),
        )

        setContent {
            OperationsTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background,
                ) {
                    OperationsApp()
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        AppGraph.get(applicationContext).notificationNavigation.acceptIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        AppGraph.get(applicationContext).onForeground()
    }
}
