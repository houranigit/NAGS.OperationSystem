window.operationsSystem = window.operationsSystem || {};

window.operationsSystem.dom = {
  setDirection(direction, language) {
    const root = document.documentElement;
    root.setAttribute("dir", direction === "rtl" ? "rtl" : "ltr");
    root.setAttribute("lang", language || "en");
  },
};

window.operationsSystem.storage = {
  get(key) {
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  set(key, value) {
    try {
      localStorage.setItem(key, value);
    } catch {
      // Storage may be unavailable (private mode/quota); persistence is best-effort.
    }
  },
  remove(key) {
    try {
      localStorage.removeItem(key);
    } catch {
      // Ignore — see set().
    }
  },
};

window.operationsSystem.api = {
  async request(method, path, body, accessToken, language, ifMatch) {
    const headers = {
      Accept: "application/json",
      "Accept-Language": language || "en",
    };

    const options = {
      method,
      headers,
      credentials: "include",
    };

    if (accessToken) {
      headers.Authorization = `Bearer ${accessToken}`;
    }

    if (ifMatch) {
      headers["If-Match"] = ifMatch;
    }

    if (body !== null && body !== undefined) {
      headers["Content-Type"] = "application/json";
      options.body = JSON.stringify(body);
    }

    const response = await fetch(`/api/v1${path}`, options);
    const text = await response.text();

    if (!response.ok) {
      throw new Error(JSON.stringify({
        status: response.status,
        body: text,
      }));
    }

    return text;
  },

  async uploadFile(path, bytes, fileName, contentType, accessToken, language, ifMatch) {
    const data = new FormData();
    data.append("file", new Blob([bytes], { type: contentType }), fileName);
    const headers = { Accept: "application/json", "Accept-Language": language || "en" };
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    if (ifMatch) headers["If-Match"] = ifMatch;
    const response = await fetch(`/api/v1${path}`, { method: "POST", headers, body: data, credentials: "include" });
    const text = await response.text();
    if (!response.ok) throw new Error(JSON.stringify({ status: response.status, body: text }));
    return text;
  },

  async requestFile(path, accessToken, language) {
    const headers = { Accept: "image/*", "Accept-Language": language || "en" };
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    const response = await fetch(`/api/v1${path}`, { headers, credentials: "include" });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(JSON.stringify({ status: response.status, body: text }));
    }
    const blob = await response.blob();
    const base64 = await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result.split(",")[1]);
      reader.onerror = reject;
      reader.readAsDataURL(blob);
    });
    return { base64, contentType: blob.type || "application/octet-stream" };
  },
};
