window.operationsSystem = window.operationsSystem || {};

window.operationsSystem.api = {
  async request(method, path, body, accessToken, language) {
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
};
