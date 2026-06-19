import axios, { type InternalAxiosRequestConfig } from 'axios'

// Access token is kept in memory only (never localStorage). The refresh token lives in an
// httpOnly cookie set by the API.
let accessToken: string | null = null

export const setAccessToken = (token: string | null) => {
  accessToken = token
}

export const getAccessToken = () => accessToken

export const api = axios.create({
  baseURL: '/api/v1',
  withCredentials: true,
})

api.interceptors.request.use((config) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`
  }
  return config
})

let refreshing: Promise<string | null> | null = null

async function refreshAccessToken(): Promise<string | null> {
  try {
    const response = await axios.post<{ accessToken: string }>(
      '/api/v1/identity/auth/refresh',
      {},
      { withCredentials: true },
    )
    setAccessToken(response.data.accessToken)
    return response.data.accessToken
  } catch {
    setAccessToken(null)
    return null
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retry?: boolean }) | undefined
    const status = error.response?.status

    if (status === 401 && original && !original._retry && !original.url?.includes('/auth/')) {
      original._retry = true
      refreshing ??= refreshAccessToken().finally(() => {
        refreshing = null
      })
      const token = await refreshing
      if (token) {
        original.headers.Authorization = `Bearer ${token}`
        return api(original)
      }
    }

    return Promise.reject(error)
  },
)

export { refreshAccessToken }
