FROM node:22.14.0-alpine AS build
WORKDIR /app
COPY package.json package-lock.json .npmrc ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

FROM nginxinc/nginx-unprivileged:1.27.4-alpine
# BFF_UPSTREAM is substituted into the template at container start
ENV BFF_UPSTREAM=http://ticketing-bff:8080
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/ticketing-frontend/browser /usr/share/nginx/html
EXPOSE 8080
