'use strict';

const jwt = require('jsonwebtoken');
const { unauthorized } = require('./errors');

function requireUser(jwtConfig) {
  return (req, res, next) => {
    const header = req.get('authorization') || '';
    const [scheme, token] = header.split(' ');
    if (scheme?.toLowerCase() !== 'bearer' || !token) {
      return next(unauthorized('A bearer token is required.'));
    }
    try {
      const claims = jwt.verify(token, jwtConfig.signingKey, {
        algorithms: ['HS256'],
        issuer: jwtConfig.issuer,
        audience: jwtConfig.audience,
        clockTolerance: 30,
      });
      req.user = { id: claims.sub, email: claims.email, role: claims.role };
      return next();
    } catch (err) {
      return next(unauthorized('Invalid or expired token.'));
    }
  };
}

module.exports = { requireUser };
