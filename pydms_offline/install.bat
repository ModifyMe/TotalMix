@echo off
echo Installing pydms and pyads from offline packages...
pip install --no-index --find-links=. pydms pyads
echo.
if %ERRORLEVEL% EQU 0 (
    echo Installation complete!
) else (
    echo Installation failed. Please check errors above.
)
pause
