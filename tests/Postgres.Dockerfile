# Isolated integration-test database, not an application deployment dependency.
FROM postgres:18-alpine@sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873
# Refresh Alpine security patches and remove the unused root-only user switcher,
# whose upstream binary embeds an outdated Go runtime. The official entrypoint
# supports starting directly as postgres with an image-owned data volume.
RUN apk upgrade --no-cache && rm /usr/local/bin/gosu
USER postgres
HEALTHCHECK --interval=5s --timeout=5s --start-period=20s CMD pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" || exit 1
