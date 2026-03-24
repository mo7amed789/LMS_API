# LMS API Contract for Frontend Integration

This document describes the current API surface exposed by the backend so the frontend team can build and test integrations quickly.

## Base URLs

- Development API URL: `https://localhost:7236`
- Health endpoint: `GET /health`
- Auth controller base path: `/api/auth`

## Authentication Model

The API uses **JWT access tokens** + **httpOnly refresh token cookie**:

1. `POST /api/auth/login`
   - Returns `{ token }` in JSON body.
   - Sets `refreshToken` cookie (httpOnly, secure, sameSite=strict, 7 days).
2. Send access token in header:
   - `Authorization: Bearer <token>`
3. When access token expires, call `POST /api/auth/refresh`
   - Requires the `refreshToken` cookie.
   - Returns a new `{ token }` and rotates the refresh cookie.
4. Logout with `POST /api/auth/logout`
   - Requires refresh cookie.
   - Revokes token and clears cookie.

> Frontend must send credentials (`withCredentials: true` in axios/fetch equivalent) for refresh/logout so cookie is included.

## CORS

Allowed frontend origin is controlled by `AppUrls:FrontendBaseUrl` in backend configuration.

## Endpoints

---

### 1) Register

- **Method/Path:** `POST /api/auth/register`
- **Auth required:** No
- **Rate limit:** No
- **Request body:**

```json
{
  "name": "string, required, max 100",
  "email": "valid email, required, max 150",
  "password": "required, 8..128 chars"
}
```

- **Success (200):**

```json
{
  "isSuccess": true,
  "message": "User registered successfully",
  "data": {
    "id": 1,
    "name": "John Doe",
    "email": "john@example.com",
    "role": "Student"
  }
}
```

- **Failure:**
  - `409` if email already exists.
  - `400` for validation errors.

---

### 2) Login

- **Method/Path:** `POST /api/auth/login`
- **Auth required:** No
- **Rate limit:** Yes (`AuthPolicy`: 5 requests / minute)
- **Request body:**

```json
{
  "email": "valid email, required",
  "password": "required, min 6"
}
```

- **Success (200):**

```json
{
  "token": "<jwt-access-token>"
}
```

- **Also sets cookie:** `refreshToken=<opaque token>; HttpOnly; Secure; SameSite=Strict`

- **Failure:**
  - `401` invalid credentials or email not verified.
  - `400` validation errors.
  - `429` too many requests.

---

### 3) Current User

- **Method/Path:** `GET /api/auth/me`
- **Auth required:** Yes (Bearer token)
- **Rate limit:** No
- **Success (200):**

```json
{
  "userId": "1",
  "email": "john@example.com",
  "role": "Student"
}
```

- **Failure:** `401` if missing/invalid token.

---

### 4) Refresh Access Token

- **Method/Path:** `POST /api/auth/refresh`
- **Auth required:** No bearer token required, but refresh cookie required.
- **Rate limit:** No
- **Request body:** none
- **Success (200):**

```json
{
  "token": "<new-jwt-access-token>"
}
```

- **Also rotates cookie:** new `refreshToken` cookie.
- **Failure:** `401` for missing/invalid/expired refresh token.

---

### 5) Logout

- **Method/Path:** `POST /api/auth/logout`
- **Auth required:** Yes (Bearer token)
- **Request body:** none
- **Success (200):** `"Logged out successfully"`
- **Failure:**
  - `400` if no refresh token cookie or invalid token.
  - `401` if access token missing/invalid.

---

### 6) Verify Email

- **Method/Path:** `GET /api/auth/verify-email?token=<token>`
- **Auth required:** No
- **Rate limit:** Yes (`AuthPolicy`)
- **Success (200):**

```json
{
  "message": "Email verified successfully"
}
```

- **Failure:** `400` invalid token.

---

### 7) Forgot Password

- **Method/Path:** `POST /api/auth/forgot-password`
- **Auth required:** No
- **Rate limit:** Yes (`AuthPolicy`)
- **Request body:**

```json
{
  "email": "valid email, required"
}
```

- **Success (200):**

```text
If the email exists, a reset link has been sent.
```

- **Note:** Response is intentionally generic to avoid account enumeration.

---

### 8) Reset Password

- **Method/Path:** `POST /api/auth/reset-password`
- **Auth required:** No
- **Rate limit:** Yes (`AuthPolicy`)
- **Request body:**

```json
{
  "token": "required",
  "newPassword": "required, 8..128 chars"
}
```

- **Success (200):** `"Password reset successful"`
- **Failure:** `400` invalid/expired token or validation errors.

## Frontend QA Checklist

1. Register a user.
2. Verify email flow (link from email).
3. Login and store bearer token in memory/state (not localStorage if avoidable).
4. Call `/api/auth/me` with bearer token.
5. Simulate token expiry then call `/api/auth/refresh` with credentials enabled.
6. Retry protected call with new access token.
7. Execute logout and ensure `/refresh` fails afterwards.
8. Validate forgot/reset-password roundtrip.
