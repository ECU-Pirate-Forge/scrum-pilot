# See https://docs.docker.com/engine/reference/builder/ for details on Dockerfile syntax

# --- Build Stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0.201 AS build
WORKDIR /src
COPY ["ScrumPilot.Web/ScrumPilot.Web.csproj", "ScrumPilot.Web/"]
RUN dotnet restore "ScrumPilot.Web/ScrumPilot.Web.csproj"
COPY . .
WORKDIR "/src/ScrumPilot.Web"
RUN dotnet publish -c Release -o /app/publish

# Fix index.html to reference the hashed blazor.webassembly file produced by publish
RUN set -eux; \
		if ls /app/publish/wwwroot/_framework/blazor.webassembly.*.js 1> /dev/null 2>&1; then \
			hashed=$(ls /app/publish/wwwroot/_framework/blazor.webassembly.*.js | head -n1); \
			base=$(basename "$hashed"); \
			sed -i "s|_framework/blazor.webassembly.js|_framework/$base|g" /app/publish/wwwroot/index.html; \
		fi

# Patch ApiBaseUrl in appsettings.json so the Blazor client calls the correct API when running in Docker
ARG API_BASE_URL=http://localhost:5219/
RUN sed -i "s|https://localhost:7195/|${API_BASE_URL}|g" /app/publish/wwwroot/appsettings.json

# Ensure runtime loader `dotnet.js` exists (some dynamic imports expect non-hashed name)
RUN set -eux; \
	# Find a suitable hashed dotnet loader and copy it to a stable name `dotnet.js`.
	for f in /app/publish/wwwroot/_framework/dotnet.*.js; do \
		if [ -f "$f" ]; then \
			# skip native/runtime variants first, then handle generic dotnet.<hash>.js
			case "$(basename "$f")" in \
				dotnet.native.*.js) continue ;; \
				dotnet.runtime.*.js) continue ;; \
				dotnet.*.js) cp "$f" /app/publish/wwwroot/_framework/dotnet.js && break ;; \
				*) cp "$f" /app/publish/wwwroot/_framework/dotnet.js && break ;; \
			esac; \
		fi; \
	done || true

# --- Runtime Stage ---
FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
# copy custom nginx config to enable SPA fallback
# nginx.conf lives in the ScrumPilot.Web folder relative to the build context
COPY ScrumPilot.Web/nginx.conf /etc/nginx/conf.d/default.conf
RUN set -eux; \
	for f in /usr/share/nginx/html/_framework/dotnet.*.js; do \
		if [ -f "$f" ]; then \
			case "$(basename "$f")" in \
				dotnet.native.*.js) continue ;; \
				dotnet.runtime.*.js) continue ;; \
				dotnet.*.js) cp "$f" /usr/share/nginx/html/_framework/dotnet.js && break ;; \
				*) cp "$f" /usr/share/nginx/html/_framework/dotnet.js && break ;; \
			esac; \
		fi; \
	done || true
EXPOSE 80
