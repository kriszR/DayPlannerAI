const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5131/api";

export async function apiGet<T>(endpoint: string): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${endpoint}`);

  if (!response.ok) {
    throw new Error(
      `API kérés sikertelen: ${response.status} ${response.statusText}`,
    );
  }

  return response.json() as Promise<T>;
}

export async function apiPut<TRequest, TResponse>(
  endpoint: string,
  data: TRequest,
): Promise<TResponse> {
  const response = await fetch(`${API_BASE_URL}${endpoint}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(data),
  });

  if (!response.ok) {
    throw new Error(
      `API kérés sikertelen: ${response.status} ${response.statusText}`,
    );
  }

  return response.json() as Promise<TResponse>;
}

export async function apiPost<TRequest, TResponse>(
  endpoint: string,
  body: TRequest,
): Promise<TResponse> {
  const response = await fetch(`${API_BASE_URL}${endpoint}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const errorBody = (await response.json().catch(() => null)) as {
      message?: string;
      title?: string;
    } | null;

    throw new Error(
      errorBody?.message ??
        errorBody?.title ??
        `API request failed: ${response.status} ${response.statusText}`,
    );
  }

  return response.json() as Promise<TResponse>;
}

export async function apiDelete(endpoint: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}${endpoint}`, {
    method: "DELETE",
  });

  if (response.ok) {
    return;
  }

  const errorBody = (await response.json().catch(() => null)) as {
    message?: string;
    title?: string;
  } | null;

  throw new Error(
    errorBody?.message ??
      errorBody?.title ??
      `API request failed: ${response.status} ${response.statusText}`,
  );
}
