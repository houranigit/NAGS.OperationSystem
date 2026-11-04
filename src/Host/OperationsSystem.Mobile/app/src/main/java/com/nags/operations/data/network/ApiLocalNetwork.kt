package com.nags.operations.data.network

import java.net.InetAddress
import java.net.URI
import java.util.Locale

/** Classifies configured local endpoints without resolving DNS on the UI thread. */
internal fun apiUsesLocalNetwork(baseUrl: String): Boolean {
    val host = runCatching { URI(baseUrl).host }.getOrNull()
        ?.trim('[', ']')?.lowercase(Locale.ROOT)?.removeSuffix(".") ?: return false
    if (host.endsWith(".local")) return true
    val ipv4 = host.split('.').map { it.toIntOrNull() }
    if (ipv4.size == 4 && ipv4.all { it != null && it in 0..255 }) {
        val first = ipv4[0]!!
        val second = ipv4[1]!!
        return first == 10 || (first == 172 && second in 16..31) ||
            (first == 192 && second == 168) || (first == 169 && second == 254) ||
            (first == 100 && second in 64..127) || first in 224..239 ||
            host == "255.255.255.255"
    }
    if (':' !in host) return false
    // A colon restricts this path to numeric IPv6 literals; getByName does not resolve hostnames here.
    val address = runCatching { InetAddress.getByName(host) }.getOrNull() ?: return false
    if (address.address.size == 4) return apiUsesLocalNetwork("http://${address.hostAddress}")
    return address.isLinkLocalAddress || address.isSiteLocalAddress || address.isMulticastAddress ||
        (address.address[0].toInt() and 0xfe) == 0xfc
}
