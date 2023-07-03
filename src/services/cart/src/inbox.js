'use strict';

const TTL_SECONDS = 7 * 24 * 3600;

// no SQL db here, so processed event ids live in redis (TTL > topic retention)
function createInbox(redis, prefix = 'cart:processed_events') {
  const key = (eventId) => `${prefix}:${eventId}`;
  return {
    async seen(eventId) {
      return (await redis.exists(key(eventId))) === 1;
    },
    async record(eventId, eventType) {
      await redis.set(key(eventId), eventType || '1', 'EX', TTL_SECONDS);
    },
  };
}

module.exports = { createInbox };
