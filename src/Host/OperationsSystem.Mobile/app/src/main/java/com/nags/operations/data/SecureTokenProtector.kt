package com.nags.operations.data

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyPermanentlyInvalidatedException
import android.security.keystore.KeyProperties
import java.security.KeyStore
import java.security.UnrecoverableKeyException
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Small Android Keystore wrapper used by [TokenStore]. Only ciphertext is written to DataStore;
 * the AES key remains non-exportable in AndroidKeyStore.
 */
internal interface TokenProtector {
    fun protect(value: String): String
    fun unprotect(value: String): String
    fun isProtected(value: String): Boolean
}

internal class AndroidKeystoreTokenProtector : TokenProtector {
    override fun protect(value: String): String = try {
        encrypt(value)
    } catch (_: KeyPermanentlyInvalidatedException) {
        // Screen-lock/key invalidation must not permanently prevent a fresh login. The old
        // ciphertext is no longer usable anyway, so replace the key and encrypt the new pair.
        resetKey()
        encrypt(value)
    } catch (_: UnrecoverableKeyException) {
        resetKey()
        encrypt(value)
    }

    private fun encrypt(value: String): String {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey())
        val ciphertext = cipher.doFinal(value.toByteArray(Charsets.UTF_8))
        return buildString {
            append(PREFIX)
            append(encoder.encodeToString(cipher.iv))
            append(SEPARATOR)
            append(encoder.encodeToString(ciphertext))
        }
    }

    override fun unprotect(value: String): String {
        // Existing installs wrote plaintext. Reading it remains supported long enough for
        // TokenStore.initializeSecureStorage() to migrate it in place.
        if (!isProtected(value)) return value

        val encoded = value.removePrefix(PREFIX).split(SEPARATOR, limit = 2)
        require(encoded.size == 2) { "Malformed protected token" }
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(
            Cipher.DECRYPT_MODE,
            getOrCreateKey(),
            GCMParameterSpec(GCM_TAG_BITS, decoder.decode(encoded[0])),
        )
        return cipher.doFinal(decoder.decode(encoded[1])).toString(Charsets.UTF_8)
    }

    override fun isProtected(value: String): Boolean = value.startsWith(PREFIX)

    private fun getOrCreateKey(): SecretKey = synchronized(keyLock) {
        val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey) ?: KeyGenerator
            .getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE)
            .apply {
                init(
                    KeyGenParameterSpec.Builder(
                        KEY_ALIAS,
                        KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
                    )
                        .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                        .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                        .setRandomizedEncryptionRequired(true)
                        .build(),
                )
            }
            .generateKey()
    }

    private fun resetKey() = synchronized(keyLock) {
        KeyStore.getInstance(KEYSTORE).apply {
            load(null)
            if (containsAlias(KEY_ALIAS)) deleteEntry(KEY_ALIAS)
        }
    }

    private companion object {
        const val KEYSTORE = "AndroidKeyStore"
        const val KEY_ALIAS = "nags.operations.mobile.auth.v1"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val GCM_TAG_BITS = 128
        const val PREFIX = "keystore:v1:"
        const val SEPARATOR = ":"

        val encoder: Base64.Encoder = Base64.getEncoder()
        val decoder: Base64.Decoder = Base64.getDecoder()
        val keyLock = Any()
    }
}
